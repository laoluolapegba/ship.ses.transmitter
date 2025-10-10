using AngleSharp.Io;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Extensions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Org.BouncyCastle.Utilities;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Services;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Infrastructure.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    /// <summary>
    /// This service processes pending FHIR sync records by sending them to the FHIR API and updating their status based on the response.
    /// The service supports filtering by resource type and handles both successful and failed sync attempts.
    /// The service also seeds pending status events for later probing if callbacks are not received in time.
    /// The service logs detailed information about the sync process, including summaries of results.
    /// </summary>
    public class FhirSyncService : IFhirSyncService
    {
        private readonly IMongoSyncRepository _repository;
        private readonly ILogger<FhirSyncService> _logger;
        private readonly IFhirApiService _fhirApiService;
        private readonly IStagingUpdateWriter _stagingUpdateWriter;
        private readonly IOptionsMonitor<FhirRoutingSettings> _routingSettings;

        public FhirSyncService(IMongoSyncRepository repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService, 
            IStagingUpdateWriter stagingUpdateWriter, IOptionsMonitor<FhirRoutingSettings> routingSettings)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
            _stagingUpdateWriter = stagingUpdateWriter;
            _routingSettings = routingSettings;

        }

        public async Task<SyncResultDto> ProcessPendingRecordsAsync<T>(CancellationToken token, string? resourceName = null) where T : FhirSyncRecord, new()
        {
            var result = new SyncResultDto();

            // Avoid double-enumeration and DB re-query; materialize once.
            var records = (await _repository.GetByStatusAsync<T>("Pending")).ToList();
            var resourceFilters = ResolveResourceFilters<T>(resourceName);
            if (resourceFilters is not null)
            {
                records = records
                    .Where(r => !string.IsNullOrWhiteSpace(r.ResourceType) && resourceFilters.Contains(r.ResourceType))
                    .ToList();
            }

            result.Total = records.Count;

            var logResourceName = DescribeResource<T>(resourceFilters); 

            _logger.LogInformation("🔎 Pending {ResourceType} records: {Count}", logResourceName, result.Total);

            if (result.Total == 0)
            {
                _logger.LogInformation("✅ No pending {ResourceType} records. Nothing to sync.", logResourceName);
                return result; // early exit
            }

            // Optional: show sample IDs at Debug
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Pending {ResourceType} sample IDs: {Ids}", logResourceName, records.Take(5).Select(r => r.ResourceId).ToArray());

            var successUpdates = new Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)>();
            var failedUpdates = new Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)>();

            var marksToSubmit = new List<StagingTransmissionMark>();
            var marksToFail = new List<long>();
            
            

            foreach (var record in records)
            {
                try
                {
                    //this is SeS callback URL, not the EMR one
                    var callbackUrl = _routingSettings.CurrentValue.Default?.CallbackUrlTemplate;

                    if (string.IsNullOrWhiteSpace(callbackUrl))
                    {
                        _logger.LogWarning("⚠️ CallbackUrlTemplate not configured. Using empty value — callbacks may fail.");
                        callbackUrl = string.Empty; 
                    }

                    _logger.LogInformation("📤 Syncing {Type} with ID {Id}", logResourceName, record.ResourceId);

                    //{    "status": "success",    "code": 202,    "message": "Request Accepted" }
                    //{"status":"success","code":202,"message":"Request accepted","transactionId":"id1_d1kVHOB34j7IUgNzsZpco7J7NTSWqDHaMe"}

                    var apiResponse = await _fhirApiService.SendAsync(
                        FhirOperation.Post,
                        record.ResourceType,
                        record.ResourceId,
                        record.FhirJson.ToCleanJson(),
                        callbackUrl,
                        record.ShipService,
                        token);

                    var accepted = apiResponse != null
                        && apiResponse.Status?.Equals("success", StringComparison.OrdinalIgnoreCase) == true
                        && apiResponse.Code == 202;

                    if (accepted)
                    { 

                        successUpdates.Add(ObjectId.Parse(record.Id), (
                            status: "Synced",
                            message: apiResponse?.Message ?? "Request accepted",
                            transactionId: apiResponse?.transactionId ?? string.Empty,
                            rawResponse: System.Text.Json.JsonSerializer.Serialize(apiResponse)
                        ));
                        _logger.LogInformation("✅ Sync success for {Id}", record.TransactionId);

                        // 🔽 seed a PENDING StatusEvent so the probe worker can check later
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(apiResponse.transactionId))
                            {
                                var pendingEvt = new StatusEvent
                                {
                                    TransactionId = apiResponse.transactionId,
                                    ResourceType = record.ResourceType, 
                                    ResourceId = record.ResourceId,
                                    ShipId = string.Empty,
                                    Status = "PENDING",
                                    Message = "Awaiting callback",
                                    ReceivedAtUtc = DateTime.UtcNow, 
                                    Source = "SHIP",
                                    Headers = null,
                                    PayloadHash = string.Empty,
                                    Data = null,  // no payload yet
                                    CorrelationId = record.CorrelationId ?? string.Empty,
                                    FacilityId = record.FacilityId ?? string.Empty,
                                    ClientId = record.ClientId,
                                    ShipService = record.ShipService,

                                    // --- Probe fields
                                    ProbeStatus = "Pending",
                                    ProbeAttempts = 0,
                                    ProbeNextAttemptAt = DateTime.UtcNow,  // worker also checks "age" via ReceivedAtUtc
                                    ProbeLastError = null
                                };

                                await _repository.InsertStatusEventAsync(pendingEvt, token);

                                _logger.LogInformation(
                                    "📬 Seeded PENDING StatusEvent for txn={Txn} {Type}/{Id} (corrId={Corr}) – will probe if no callback in time.",
                                    pendingEvt.TransactionId, pendingEvt.ResourceType, pendingEvt.TransactionId, pendingEvt.CorrelationId);
                            }
                            else
                            {
                                // We can’t probe without a concrete resourceId for GET /{type}/{id}
                                _logger.LogWarning("⚠️ Skipped seeding PENDING StatusEvent: missing API TransactionId for {Type} (corrId={Corr}).",
                                    record.ResourceType, record.CorrelationId ?? record.TransactionId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "❌ Failed to create PENDING StatusEvent for {Type}/{Id} (txn={Txn}). Probe fallback disabled for this item.",
                                record.ResourceType, record.TransactionId, apiResponse?.transactionId);
                        }

                        if (record.StagingId.HasValue)
                            marksToSubmit.Add(new StagingTransmissionMark(record.StagingId.Value, apiResponse!.transactionId, DateTime.UtcNow));
                    }
                    else
                    {
                        failedUpdates.Add(ObjectId.Parse(record.Id), (
                            status: "Failed",
                            message: apiResponse?.Message ?? "Unsuccessful response",
                            transactionId: apiResponse?.transactionId,
                            rawResponse: System.Text.Json.JsonSerializer.Serialize(apiResponse)
                        ));
                        _logger.LogWarning("❌ API returned error for {Id}: {Message}", record.TransactionId, apiResponse?.Message);

                        if (record.StagingId.HasValue)
                            marksToFail.Add(record.StagingId.Value);
                    }
                }
                catch (Exception ex)
                {
                    failedUpdates.Add(ObjectId.Parse(record.Id), (
                        status: "Failed",
                        message: ex.Message,
                        transactionId: null,
                        rawResponse: System.Text.Json.JsonSerializer.Serialize(new { Error = ex.Message, StackTrace = ex.StackTrace })
                    ));

                    if (record.StagingId.HasValue)
                        marksToFail.Add(record.StagingId.Value);

                    _logger.LogError(ex, "❌ Sync failed for {Id}: {Message}", record.ResourceId, ex.Message);
                }
            }

            // Persist results
            if (successUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(successUpdates);

            if (failedUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(failedUpdates);

            result.Synced = successUpdates.Count;
            result.Failed = failedUpdates.Count;
            result.FailedIds = failedUpdates.Keys.Select(x => x.ToString()).ToList();

            await _stagingUpdateWriter.BulkMarkSubmittedAsync(marksToSubmit, token);
            await _stagingUpdateWriter.BulkMarkFailedAsync(marksToFail, token);

            // 📊 Summary (you also log from the worker, but this helps if the caller changes later)
            _logger.LogInformation("📊 Sync result for {ResourceType}: Total={Total}, Synced={Synced}, Failed={Failed}",
                logResourceName, result.Total, result.Synced, result.Failed);

            return result;
        }

        private static HashSet<string>? ResolveResourceFilters<T>(string? resourceName) where T : FhirSyncRecord, new()
        {
            var names = ResolveResourceNames<T>(resourceName);
            return names.Count == 0 ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyCollection<string> ResolveResourceNames<T>(string? resourceName) where T : FhirSyncRecord, new()
        {
            if (!string.IsNullOrWhiteSpace(resourceName))
                return new[] { resourceName };

            var attrs = typeof(T).GetCustomAttributes(typeof(FhirResourceAttribute), false)
                .OfType<FhirResourceAttribute>()
                .Select(a => a.ResourceName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();

            if (attrs.Length > 0)
                return attrs;

            var instance = new T();
            if (!string.IsNullOrWhiteSpace(instance.ResourceType))
                return new[] { instance.ResourceType };

            var fallback = typeof(T).Name;
            if (fallback.EndsWith("SyncRecord", StringComparison.OrdinalIgnoreCase))
                fallback = fallback[..^"SyncRecord".Length];

            return string.IsNullOrWhiteSpace(fallback)
                ? Array.Empty<string>()
                : new[] { fallback };
        }

        private static string DescribeResource<T>(HashSet<string>? resourceFilters)
        {
            if (resourceFilters is null || resourceFilters.Count == 0)
                return typeof(T).Name;

            if (resourceFilters.Count == 1)
                return resourceFilters.First();

            const int previewLimit = 5; // how many to show
            var list = resourceFilters.Take(previewLimit).ToArray();
            var prefix = string.Join(", ", list);
            var remaining = resourceFilters.Count - previewLimit;

            return remaining > 0
                ? $"{prefix}, (+{remaining} more)"
                : prefix;
        }


        private static string BuildCallbackUrl(string template, FhirSyncRecord record)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            string safe(string s) => string.IsNullOrEmpty(s) ? "" : Uri.EscapeDataString(s);

            var url = template
                .Replace("{facilityId}", safe(record.FacilityId), StringComparison.OrdinalIgnoreCase)
                .Replace("{resourceType}", safe(record.ResourceType), StringComparison.OrdinalIgnoreCase)
                .Replace("{resourceId}", safe(record.ResourceId), StringComparison.OrdinalIgnoreCase)
                .Replace("{transactionId}", safe(record.TransactionId), StringComparison.OrdinalIgnoreCase);

            return Uri.TryCreate(url, UriKind.Absolute, out var _) ? url : null;
        }
        //public async Task ProcessPendingRecordsAsync<T>(CancellationToken cancellationToken) where T : FhirSyncRecord
        //{
        //    _logger.LogInformation($"Getting pending records ...");
        //    var records = await _repository.GetPendingRecordsAsync<T>();


        //    _logger.LogInformation($"Got {records.Count()} pending records from {resourceType.ToString()}...");
        //    foreach (var record in records) 
        //    {
        //        if (cancellationToken.IsCancellationRequested)
        //            break;

        //        try
        //        {
        //            _logger.LogInformation($"Processing {record.ResourceId}... from {resourceType.ToString()}");
        //            // Simulate FHIR API Call
        //            //string jsonPayload = record.FhirJson.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
        //            string jsonPayload = record.FhirJson.ToCleanJson();
        //            await _fhirApiService.SendAsync(
        //                FhirOperation.Post,
        //                resourceType: "Patient",
        //                resourceId: record.ResourceId,
        //                jsonPayload: jsonPayload,
        //                cancellationToken: cancellationToken
        //            );
        //            await Task.Delay(1000, cancellationToken);

        //            record.Status = "Synced";
        //            record.TimeSynced = DateTime.UtcNow;
        //            await _repository.UpdateRecordAsync(record);
        //            _logger.LogInformation($"Updated resource {record.ResourceId} ..."); 

        //        }
        //        catch (Exception ex)
        //        {
        //            record.RetryCount++;
        //            record.ErrorMessage = ex.Message;
        //            await _repository.UpdateRecordAsync(record);
        //        }
        //    }
        //}
    }
}
