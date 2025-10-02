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
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public class FhirSyncService : IFhirSyncService
    {
        private readonly IMongoSyncRepository _repository;
        private readonly ILogger<FhirSyncService> _logger;
        private readonly IFhirApiService _fhirApiService;
        private readonly IOptionsMonitor<FhirApiSettings> _apiSettings;
        private readonly IStagingUpdateWriter _stagingUpdateWriter;

        public FhirSyncService(IMongoSyncRepository repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService, 
            IOptionsMonitor<FhirApiSettings> apiSettings, IStagingUpdateWriter stagingUpdateWriter)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
            _apiSettings = apiSettings;
            _stagingUpdateWriter = stagingUpdateWriter;
        }

        public async Task<SyncResultDto> ProcessPendingRecordsAsync<T>(CancellationToken token) where T : FhirSyncRecord, new()
        {
            //var records = await _repository.GetPendingRecordsAsync<T>();

            var result = new SyncResultDto();

            var records = await _repository.GetByStatusAsync<T>("Pending");
            result.Total = records.Count();

            var successIds = new List<string>();
            var failedIds = new List<string>();

            var successUpdates = new Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)>();
            var failedUpdates = new Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)>();

            var marksToSubmit = new List<StagingTransmissionMark>();
            var marksToFail = new List<long>();

            foreach (var record in records)
            {
                try
                {
                    var callbackUrl = _apiSettings.CurrentValue.CallbackUrlTemplate;
                    if (string.IsNullOrWhiteSpace(callbackUrl))
                        throw new InvalidOperationException("FhirApi:CallbackUrlTemplate is missing or produced an empty URL.");

                    _logger.LogInformation("📤 Syncing {Type} with ID {Id}", typeof(T).Name, record.ResourceId);

                    var apiResponse = await _fhirApiService.SendAsync(
                    FhirOperation.Post,
                    record.ResourceType,
                    record.ResourceId,
                    record.FhirJson.ToCleanJson(),
                    callbackUrl,
                    token);


                    //  Interpret the FHIR API response
                    if (apiResponse != null && apiResponse.Status?.ToLower() == "success" && apiResponse.Code == 202)
                    {
                        successUpdates.Add(ObjectId.Parse(record.Id), (
                            status: "Synced",
                            message: apiResponse?.Message ?? "Request accepted",
                            transactionId: apiResponse?.transactionId ?? string.Empty,
                            rawResponse: System.Text.Json.JsonSerializer.Serialize(apiResponse)
                        ));
                        _logger.LogInformation("Sync success for {Id}", record.ResourceId);
                    }
                    else
                    {
                        failedUpdates.Add(ObjectId.Parse(record.Id), (
                            status: "Failed",
                            message: apiResponse?.Message ?? "Unsuccessful response",
                            transactionId: apiResponse?.transactionId,
                            rawResponse: System.Text.Json.JsonSerializer.Serialize(apiResponse)
                        ));
                        _logger.LogWarning("❌ API returned error for {Id}: {Message}", record.ResourceId, apiResponse?.Message);
                    }


                    var accepted = apiResponse != null
                       && apiResponse.Status?.Equals("success", StringComparison.OrdinalIgnoreCase) == true
                       && apiResponse.Code == 202;

                    if (record.StagingId.HasValue)
                    {
                        if (accepted)
                        {
                            marksToSubmit.Add(new StagingTransmissionMark(
                                record.StagingId.Value,
                                apiResponse.transactionId,
                                DateTime.UtcNow));
                        }
                        else
                        {
                            marksToFail.Add(record.StagingId.Value);
                        }
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

            // Persist results to Mongo
            if (successUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(successUpdates);

            if (failedUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(failedUpdates);

            result.Synced = successUpdates.Count;
            result.Failed = failedUpdates.Count;
            result.FailedIds = failedUpdates.Keys.Select(x => x.ToString()).ToList();

            await _stagingUpdateWriter.BulkMarkSubmittedAsync(marksToSubmit, token);
            await _stagingUpdateWriter.BulkMarkFailedAsync(marksToFail, token);
            return result;

            //if (successIds.Any())
            //{
            //    var objectIds = successIds.Select(id => ObjectId.Parse(id)).ToList();
            //    await _repository.BulkUpdateStatusAsync<T>(objectIds, "Synced");
            //}

            //if (failedIds.Any())
            //{
            //    var objectIds = failedIds.Select(id => ObjectId.Parse(id)).ToList();
            //    await _repository.BulkUpdateStatusAsync<T>(objectIds, "Failed");
            //}
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
