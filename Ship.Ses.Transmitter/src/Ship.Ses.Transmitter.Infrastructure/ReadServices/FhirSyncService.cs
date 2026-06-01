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

        // Total send attempts a record gets before it is permanently Failed. Until then a failed
        // attempt is requeued (left Pending) for the next cycle. (Finding 3.5 — bounded retry.)
        private const int MaxSendAttempts = 3;

        private readonly IFhirSyncStore _repository;
        private readonly ILogger<FhirSyncService> _logger;
        private readonly IFhirApiService _fhirApiService;
        private readonly IStagingUpdateWriter _stagingUpdateWriter;
        private readonly IOptionsMonitor<FhirRoutingSettings> _routingSettings;
        private readonly IClientCredentialProvider _credentials;

        public FhirSyncService(IFhirSyncStore repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService,
            IStagingUpdateWriter stagingUpdateWriter, IOptionsMonitor<FhirRoutingSettings> routingSettings,
            IClientCredentialProvider credentials)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
            _stagingUpdateWriter = stagingUpdateWriter;
            _routingSettings = routingSettings;
            _credentials = credentials;

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

            var logResourceName = DescribeResource<T>(resourceFilters);

            // Process only clients we can authenticate. Vault mode: the active client set discovered at
            // startup; unknown or inactive/revoked clients are left Pending (a restart is required to pick up
            // newly added clients). Config mode: every non-blank clientId is known. (Valid-clients-only.)
            var known = new List<T>(records.Count);
            var unknownClients = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (_credentials.IsClientKnown(r.ClientId)) known.Add(r);
                else unknownClients.Add(string.IsNullOrWhiteSpace(r.ClientId) ? "<none>" : r.ClientId!);
            }
            if (unknownClients.Count > 0)
                _logger.LogWarning("⏭️ Skipped {Count} {Type} record(s) for unknown/inactive client(s) [{Clients}]; left Pending (restart to load newly added clients).",
                    records.Count - known.Count, logResourceName, string.Join(", ", unknownClients));
            records = known;

            result.Total = records.Count;

            _logger.LogInformation("🔎 Pending {Type} records: {Count}", logResourceName, result.Total);
            if (result.Total == 0) return result;

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Pending {Type} sample IDs: {Ids}", logResourceName,
                    records.Take(5).Select(r => r.ResourceId).ToArray());

            //Accumulators (keyed by record id; storage-neutral). One combined map: the adapter applies
            // whatever Status each update carries (Synced / Pending-requeue / permanent Failed).
            var updates = new Dictionary<string, RecordStatusUpdate>();
            var marksToSubmit = new List<StagingTransmissionMark>();
            var marksToFail = new List<long>();
            var failedIds = new List<string>();
            var synced = 0;
            var requeued = 0;
            var failed = 0;

            // Records a failed send attempt: requeue (leave Pending) until the attempt cap, then
            // permanently Fail. RetryCount is incremented on every attempt via IncrementRetry. (Finding 3.5)
            async Task RecordFailedAttemptAsync(FhirSyncRecord record, string message, string? txn, string raw, bool seedErrorOnPermanent)
            {
                var attempts = record.RetryCount + 1;
                if (attempts >= MaxSendAttempts)
                {
                    updates[record.Id] = new RecordStatusUpdate("Failed", message, txn ?? string.Empty, raw, IncrementRetry: true);
                    if (record.StagingId.HasValue)
                        marksToFail.Add(record.StagingId.Value);
                    failed++;
                    failedIds.Add(record.Id);

                    if (seedErrorOnPermanent)
                        await TrySeedErrorAsync(record, token, message: message);

                    _logger.LogWarning("❌ ResourceId={ResourceId} permanently FAILED after {Attempts} attempt(s): {Message}",
                        record.ResourceId, attempts, message);
                }
                else
                {
                    updates[record.Id] = new RecordStatusUpdate("Pending", message, txn ?? string.Empty, raw, IncrementRetry: true);
                    requeued++;
                    _logger.LogWarning("🔁 ResourceId={ResourceId} failed (attempt {Attempts}/{Max}); requeued for retry: {Message}",
                        record.ResourceId, attempts, MaxSendAttempts, message);
                }
            }

            // Round-robin across clients so a high-volume client cannot starve others (Finding 3.4):
            // process one record per active client per round. Each client keeps its own circuit breaker.
            var queues = records
                .GroupBy(r => r.ClientId ?? string.Empty)
                .ToDictionary(g => g.Key, g => new Queue<FhirSyncRecord>(g.Cast<FhirSyncRecord>()));
            var clients = queues.Keys.ToList();
            var consecutiveErrors = clients.ToDictionary(c => c, _ => 0);
            var breakerOpen = clients.ToDictionary(c => c, _ => false);

            var progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (var clientId in clients)
                {
                    var queue = queues[clientId];
                    if (queue.Count == 0) continue;
                    var record = queue.Dequeue();
                    progressed = true;

                    // Per-record audit/trace scope (carries through to all logs + StatusEvents below)
                    using var recordScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["ClientId"] = record.ClientId ?? string.Empty,
                        ["FacilityId"] = record.FacilityId ?? string.Empty,
                        ["ShipService"] = record.ShipService ?? string.Empty,
                        ["ResourceId"] = record.ResourceId ?? string.Empty
                    });

                    // Per-client circuit breaker: leave the rest of THIS client's records Pending so they
                    // are retried next cycle (other clients are unaffected; nothing is permanently failed here).
                    if (breakerOpen[clientId])
                    {
                        _logger.LogWarning("⏭️ Skipping ResourceId={ResourceId}: client '{ClientId}' circuit open after {Threshold} consecutive failures; left Pending for retry.",
                            record.ResourceId, clientId, ClientBreakerThreshold);
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
                        consecutiveErrors[clientId] = 0;

                        var responseRaw = apiResponse?.Raw ?? "{}";
                        var responseMsg = apiResponse?.Message ?? "Unsuccessful response";
                        var responseTxn = string.IsNullOrWhiteSpace(apiResponse?.transactionId) ? null : apiResponse!.transactionId;

                        var accepted = apiResponse is not null
                                       && string.Equals(apiResponse.Status, "success", StringComparison.OrdinalIgnoreCase)
                                       && apiResponse.Code == 202;

                        var isBundle = apiResponse?.Data != null && apiResponse.Data.Count > 0;
                        string? representativeTxn = null;

                        // If PDS returned bundle-style items, process them
                        if (isBundle)
                        {
                            var idx = 0;
                            foreach (var item in apiResponse!.Data!)
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

                            // If accepted => seed pending. (Error events are seeded on permanent failure below,
                            // not on every transient retry.)
                            if (accepted && !string.IsNullOrWhiteSpace(representativeTxn))
                                await TrySeedPendingAsync(record, representativeTxn!, token);
                        }

                        // Persist record outcome (success and failure are mutually exclusive)
                        if (!accepted)
                        {
                            // Bundle item-level errors were already seeded above; avoid a duplicate record-level event.
                            await RecordFailedAttemptAsync(record, responseMsg, representativeTxn, responseRaw, seedErrorOnPermanent: !isBundle);
                            _logger.LogWarning("❌ API error for ResourceId={ResourceId}: {Message}", record.ResourceId, responseMsg);
                        }
                        else
                        {
                            updates[record.Id] = new RecordStatusUpdate("Synced", apiResponse?.Message ?? "Request accepted", representativeTxn ?? string.Empty, responseRaw);

                            if (record.StagingId.HasValue)
                                marksToSubmit.Add(new StagingTransmissionMark(record.StagingId.Value, representativeTxn ?? string.Empty, DateTime.UtcNow));

                            synced++;
                            _logger.LogInformation("✅ Accepted ResourceId={ResourceId} ({Type}). Txn={Txn}",
                                record.ResourceId, normalizedType, representativeTxn ?? "<none>");
                        }
                    }
                    catch (Exception ex)
                    {
                        await RecordFailedAttemptAsync(record, ex.Message, null, $"{{\"error\":\"{ex.Message}\"}}", seedErrorOnPermanent: true);

                        _logger.LogError(ex, "❌ Sync failed for ResourceId={ResourceId}", record.ResourceId);

                        // Trip the per-client breaker after repeated transport/auth failures.
                        if (++consecutiveErrors[clientId] >= ClientBreakerThreshold)
                        {
                            breakerOpen[clientId] = true;
                            _logger.LogError("⛔ Client {ClientId} circuit opened after {Count} consecutive failures; remaining records left Pending this batch.",
                                clientId, consecutiveErrors[clientId]);
                        }
                    }
                }
            }

            // Persist + summarize
            if (updates.Count > 0)
                await _repository.BulkUpdateStatusAsync<T>(updates);

            result.Synced = synced;
            result.Requeued = requeued;
            result.Failed = failed;
            result.FailedIds = failedIds;

            if (marksToSubmit.Count > 0)
                await _stagingUpdateWriter.BulkMarkSubmittedAsync(marksToSubmit, token);
            if (marksToFail.Count > 0)
                await _stagingUpdateWriter.BulkMarkFailedAsync(marksToFail, token);

            _logger.LogInformation("📊 Sync result for {Type}: Total={Total}, Synced={Synced}, Requeued={Requeued}, Failed={Failed}",
                logResourceName, result.Total, result.Synced, result.Requeued, result.Failed);

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
