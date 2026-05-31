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
using Ship.Ses.Transmitter.Domain.SyncModels;
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
using System.Text.Json;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    /// <summary>
    /// This service processes pending FHIR sync records by sending them to the FHIR API and updating their status based on the response.
    /// The service supports filtering by resource type and handles both successful and failed sync attempts.
    /// Records are processed grouped by clientId so one client's outage cannot block or starve others;
    /// each client group has a small consecutive-failure circuit breaker.
    /// The service also seeds pending status events for later probing if callbacks are not received in time.
    /// </summary>
    public class FhirSyncService : IFhirSyncService
    {
        // After this many consecutive transport/auth failures for a client, skip the rest of that
        // client's records for the current batch (other clients are unaffected).
        private const int ClientBreakerThreshold = 3;

        private readonly IFhirSyncStore _repository;
        private readonly ILogger<FhirSyncService> _logger;
        private readonly IFhirApiService _fhirApiService;
        private readonly IStagingUpdateWriter _stagingUpdateWriter;
        private readonly IOptionsMonitor<FhirRoutingSettings> _routingSettings;

        public FhirSyncService(IFhirSyncStore repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService,
            IStagingUpdateWriter stagingUpdateWriter, IOptionsMonitor<FhirRoutingSettings> routingSettings)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
            _stagingUpdateWriter = stagingUpdateWriter;
            _routingSettings = routingSettings;

        }

        public async Task<SyncResultDto> ProcessPendingRecordsAsync<T>(
    CancellationToken token, string? resourceName = null) where T : FhirSyncRecord, new()
        {
            var result = new SyncResultDto();

            //Load + optional filter
            var records = (await _repository.GetByStatusAsync<T>("Pending")).ToList();
            var resourceFilters = ResolveResourceFilters<T>(resourceName);
            if (resourceFilters is not null)
                records = records
                    .Where(r => !string.IsNullOrWhiteSpace(r.ResourceType) && resourceFilters.Contains(r.ResourceType))
                    .ToList();

            result.Total = records.Count;
            var logResourceName = DescribeResource<T>(resourceFilters);

            _logger.LogInformation("🔎 Pending {Type} records: {Count}", logResourceName, result.Total);
            if (result.Total == 0) return result;

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Pending {Type} sample IDs: {Ids}", logResourceName,
                    records.Take(5).Select(r => r.ResourceId).ToArray());

            //Accumulators (keyed by record id; storage-neutral)
            var successUpdates = new Dictionary<string, RecordStatusUpdate>();
            var failedUpdates = new Dictionary<string, RecordStatusUpdate>();
            var marksToSubmit = new List<StagingTransmissionMark>();
            var marksToFail = new List<long>();

            // Process records grouped by client so one client's outage cannot block others.
            foreach (var clientGroup in records.GroupBy(r => r.ClientId ?? string.Empty))
            {
                var groupClientId = clientGroup.Key;
                var consecutiveErrors = 0;
                var breakerOpen = false;

                foreach (var record in clientGroup)
                {
                    // Per-record audit/trace scope (carries through to all logs + StatusEvents below)
                    using var recordScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["ClientId"] = record.ClientId ?? string.Empty,
                        ["FacilityId"] = record.FacilityId ?? string.Empty,
                        ["ShipService"] = record.ShipService ?? string.Empty,
                        ["ResourceId"] = record.ResourceId ?? string.Empty
                    });

                    // Per-client circuit breaker: fast-fail the rest of THIS client's batch.
                    if (breakerOpen)
                    {
                        failedUpdates[record.Id] = new RecordStatusUpdate("Failed",
                            $"Skipped: client '{groupClientId}' circuit open after {ClientBreakerThreshold} consecutive failures",
                            "", "{}");
                        if (record.StagingId.HasValue)
                            marksToFail.Add(record.StagingId.Value);
                        continue;
                    }

                    try
                    {
                        var payload = record.FhirJson.ToCleanJson();
                        var normalizedType = TryExtractResourceType(payload) ?? record.ResourceType ?? "Resource";
                        var callbackUrl = _routingSettings.CurrentValue.Default?.CallbackUrlTemplate;

                        _logger.LogInformation("📤 Syncing {Type} record ResourceId={ResourceId} (ShipService={Service}, Client={ClientId})",
                            normalizedType, record.ResourceId, record.ShipService, record.ClientId);

                        _logger.LogDebug("➡️ Detected payload type: {ResourceType}", normalizedType);

                        var apiResponse = await _fhirApiService.SendAsync(
                            FhirOperation.Post,
                            clientId: record.ClientId,
                            resourceType: normalizedType,
                            resourceId: record.ResourceId,
                            jsonPayload: payload,
                            callbackUrl: callbackUrl,
                            shipService: record.ShipService,
                            cancellationToken: token);

                        // The send completed (transport + auth OK), even if the API rejected it → reset breaker.
                        consecutiveErrors = 0;

                        var responseRaw = apiResponse?.Raw ?? "{}";
                        var responseMsg = apiResponse?.Message ?? "Unsuccessful response";
                        var responseTxn = string.IsNullOrWhiteSpace(apiResponse?.transactionId) ? null : apiResponse!.transactionId;

                        var accepted = apiResponse is not null
                                       && string.Equals(apiResponse.Status, "success", StringComparison.OrdinalIgnoreCase)
                                       && apiResponse.Code == 202;

                        string? representativeTxn = null;

                        // If PDS returned bundle-style items, process them
                        if (apiResponse?.Data != null && apiResponse.Data.Count > 0)
                        {
                            var idx = 0;
                            foreach (var item in apiResponse.Data)
                            {
                                var itemStatus = item.Status;
                                var itemMessage = item.Message;
                                var itemTxn = string.IsNullOrWhiteSpace(item.TransactionId) ? null : item.TransactionId;
                                var itemId = item.Id;

                                if (idx == 0 && !string.IsNullOrWhiteSpace(itemTxn))
                                    representativeTxn = itemTxn;

                                // Accepted item => seed pending; non-accepted item => seed error (optional)
                                if (string.Equals(itemStatus, "accepted", StringComparison.OrdinalIgnoreCase) &&
                                    !string.IsNullOrWhiteSpace(itemTxn))
                                {
                                    await TrySeedPendingAsync(record, itemTxn!, token, resourceIdOverride: itemId);
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Bundle item not accepted (idx={Idx}) for ResourceId={ResourceId}: {Msg}",
                                        idx, record.ResourceId, itemMessage ?? "no message");

                                    // Optional: persist an ERROR status event so you can callback/report later
                                    await TrySeedErrorAsync(record, token,
                                        message: itemMessage ?? "Bundle item rejected",
                                        resourceIdOverride: itemId);
                                }

                                idx++;
                            }

                            // If no representativeTxn was found, fall back to top-level txn if any
                            representativeTxn ??= responseTxn;
                        }
                        else
                        {
                            // Single response: use top-level transactionId if present
                            representativeTxn = responseTxn;

                            // If accepted => seed pending. If not accepted => seed error (optional)
                            if (accepted && !string.IsNullOrWhiteSpace(representativeTxn))
                            {
                                await TrySeedPendingAsync(record, representativeTxn!, token);
                            }
                            else if (!accepted)
                            {
                                await TrySeedErrorAsync(record, token, message: responseMsg);
                            }
                        }

                        // Persist record outcome (success and failure are mutually exclusive)
                        if (!accepted)
                        {
                            failedUpdates[record.Id] = new RecordStatusUpdate("Failed", responseMsg, representativeTxn ?? "", responseRaw);

                            if (record.StagingId.HasValue)
                                marksToFail.Add(record.StagingId.Value);

                            _logger.LogWarning("❌ API error for ResourceId={ResourceId}: {Message}", record.ResourceId, responseMsg);
                        }
                        else
                        {
                            successUpdates[record.Id] = new RecordStatusUpdate("Synced", apiResponse?.Message ?? "Request accepted", representativeTxn ?? "", responseRaw);

                            if (record.StagingId.HasValue)
                                marksToSubmit.Add(new StagingTransmissionMark(record.StagingId.Value, representativeTxn ?? "", DateTime.UtcNow));

                            _logger.LogInformation("✅ Accepted ResourceId={ResourceId} ({Type}). Txn={Txn}",
                                record.ResourceId, normalizedType, representativeTxn ?? "<none>");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedUpdates[record.Id] = new RecordStatusUpdate("Failed", ex.Message, "", $"{{\"error\":\"{ex.Message}\"}}");

                        if (record.StagingId.HasValue)
                            marksToFail.Add(record.StagingId.Value);

                        _logger.LogError(ex, "❌ Sync failed for ResourceId={ResourceId}", record.ResourceId);

                        // Trip the per-client breaker after repeated transport/auth failures.
                        if (++consecutiveErrors >= ClientBreakerThreshold)
                        {
                            breakerOpen = true;
                            _logger.LogError("⛔ Client {ClientId} circuit opened after {Count} consecutive failures; skipping remaining records this batch.",
                                groupClientId, consecutiveErrors);
                        }
                    }
                }
            }

            // Persist + summarize
            if (successUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(successUpdates);

            if (failedUpdates.Any())
                await _repository.BulkUpdateStatusAsync<T>(failedUpdates);

            result.Synced = successUpdates.Count;
            result.Failed = failedUpdates.Count;
            result.FailedIds = failedUpdates.Keys.ToList();

            if (marksToSubmit.Count > 0)
                await _stagingUpdateWriter.BulkMarkSubmittedAsync(marksToSubmit, token);
            if (marksToFail.Count > 0)
                await _stagingUpdateWriter.BulkMarkFailedAsync(marksToFail, token);

            _logger.LogInformation("📊 Sync result for {Type}: Total={Total}, Synced={Synced}, Failed={Failed}",
                logResourceName, result.Total, result.Synced, result.Failed);

            return result;
        }

        // ——— helpers ———

        private static string? TryExtractResourceType(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("resourceType", out var rt) && rt.ValueKind == JsonValueKind.String)
                    return rt.GetString();
            }
            catch { /* ignore */ }
            return null;
        }

        // Seed a pending event; allow overriding resourceId for per-item events from a bundle
        private async Task TrySeedPendingAsync(FhirSyncRecord rec, string txn, CancellationToken token, string? resourceIdOverride = null)
        {
            try
            {
                var evt = new StatusEvent
                {
                    TransactionId = txn,
                    ResourceType = rec.ResourceType,                // you can refine if you store per-item type
                    ResourceId = resourceIdOverride ?? rec.ResourceId,
                    ShipId = string.Empty,
                    Status = "PENDING",
                    Message = "Awaiting callback",
                    ReceivedAtUtc = DateTime.UtcNow,
                    Source = "SHIP",
                    Headers = null,
                    PayloadHash = string.Empty,
                    Data = null,
                    CorrelationId = rec.CorrelationId ?? string.Empty,
                    FacilityId = rec.FacilityId ?? string.Empty,
                    ClientId = rec.ClientId,
                    ShipService = rec.ShipService,

                    // Persist the EMR callback target now so EmrCallbackWorker doesn't need a
                    // patient-only lookup later (works for non-patient resources too). (Finding 5.2)
                    EmrTargetUrl = rec.ClientEMRCallbackUrl,

                    // probe fields, if used in your repo
                    ProbeStatus = "Pending",
                    ProbeAttempts = 0,
                    ProbeNextAttemptAt = DateTime.UtcNow,
                    ProbeLastError = null
                };

                await _repository.InsertStatusEventAsync(evt, token);
                _logger.LogInformation("📬 Seeded PENDING StatusEvent txn={Txn} resId={ResId}", txn, evt.ResourceId ?? "<null>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create PENDING StatusEvent for txn={Txn}", txn);
            }
        }
        private async Task TrySeedErrorAsync(FhirSyncRecord rec, CancellationToken token, string message, string? resourceIdOverride = null)
        {
            try
            {
                var evt = new StatusEvent
                {
                    TransactionId = string.Empty,                    // none on hard failures
                    ResourceType = rec.ResourceType,
                    ResourceId = resourceIdOverride ?? rec.ResourceId,
                    ShipId = string.Empty,
                    Status = "ERROR",
                    Message = message,
                    ReceivedAtUtc = DateTime.UtcNow,
                    Source = "SHIP",
                    Headers = null,
                    PayloadHash = rec.PayloadHash ?? string.Empty,
                    Data = null,
                    CorrelationId = rec.CorrelationId ?? string.Empty,
                    FacilityId = rec.FacilityId ?? string.Empty,
                    ClientId = rec.ClientId,
                    ShipService = rec.ShipService
                };

                await _repository.InsertStatusEventAsync(evt, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create ERROR StatusEvent for ResourceId={ResourceId}.", rec.ResourceId);
            }
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
    }
}
