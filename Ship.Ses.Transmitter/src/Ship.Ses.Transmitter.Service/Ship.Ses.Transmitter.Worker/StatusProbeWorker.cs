using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    using Microsoft.Extensions.Options;
    using MongoDB.Bson;
    using Ship.Ses.Transmitter.Application.Sync;
    using Ship.Ses.Transmitter.Domain.Enums;
    using Ship.Ses.Transmitter.Domain.Patients;
    using Ship.Ses.Transmitter.Domain.Sync;
    using Ship.Ses.Transmitter.Domain.SyncModels;
    using Ship.Ses.Transmitter.Infrastructure.Settings;
    using System.Net.Http.Headers;
    using System.Text.Json;

    /// <summary>
    /// Periodically probes SHIP for resources that didn't trigger a callback within TimeoutSeconds.
    /// For 200/success -> writes a SUCCESS StatusEvent (Source="PROBE") with payload.
    /// For 404 -> does nothing further (no new event), just marks probe flow complete for that item.
    /// Other/exception -> bounded retries with simple backoff, then Abandon.
    /// </summary>
    public sealed class StatusProbeWorker : BackgroundService
    {
        private readonly ILogger<StatusProbeWorker> _logger;
        private readonly IMongoSyncRepository _repo;
        private readonly IFhirApiService _fhir;
        private readonly StatusProbeSettings _opt;

        public StatusProbeWorker(
            ILogger<StatusProbeWorker> logger,
            IMongoSyncRepository repo,
            IFhirApiService fhir,
            IOptions<StatusProbeSettings> opt)
        {
            _logger = logger;
            _repo = repo;
            _fhir = fhir;
            _opt = opt.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_opt.Enabled)
            {
                _logger.LogInformation("⏸️ StatusProbe disabled by config.");
                return;
            }

            var poll = TimeSpan.FromSeconds(_opt.PollIntervalSeconds);
            var age = TimeSpan.FromSeconds(_opt.TimeoutSeconds);

            _logger.LogInformation("🛰️ StatusProbeWorker started. timeout={Timeout}s poll={Poll}s batch={Batch} maxAttempts={Max}",
                age.TotalSeconds, poll.TotalSeconds, _opt.BatchSize, _opt.MaxAttempts);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var candidates = await _repo.FetchDueStatusProbesAsync(age, _opt.BatchSize, stoppingToken);

                    if (candidates.Count == 0)
                    {
                        _logger.LogDebug("⏳ No due probes.");
                        await Task.Delay(poll, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("🔎 Found {Count} PENDING event(s) past timeout to probe.", candidates.Count);

                    // Process sequentially 
                    foreach (var ev in candidates)
                    {
                        if (!await _repo.TryMarkProbeInFlightAsync(ev.Id, stoppingToken))
                            continue;

                        await ProbeOneAsync(ev, stoppingToken);
                    }
                }
                catch (OperationCanceledException) { /* shutdown */ }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "💥 Unhandled error in StatusProbe loop.");
                }

                await Task.Delay(poll, stoppingToken);
            }
        }

        private async Task ProbeOneAsync(StatusEvent ev, CancellationToken ct)
        {
            var attempt = ev.ProbeAttempts + 1;

            try
            {
                if (string.IsNullOrWhiteSpace(ev.ResourceType) || string.IsNullOrWhiteSpace(ev.ResourceId))
                {
                    _logger.LogWarning("⚠️ Probe skipped: missing ResourceType/ResourceId for transactionId={TransactionId}",
                        ev.TransactionId);
                    await _repo.MarkProbeRetryAsync(ev.Id, "Missing resource identifiers", TimeSpan.FromMinutes(5), abandon: true, ct);
                    return;
                }

                _logger.LogInformation(
                    "➡️ Probing status for {ResourceType}/{ResourceId} (txn={Txn}, corrId={Corr}, attempt={Attempt})",
                    ev.ResourceType, ev.ResourceId, ev.TransactionId, ev.CorrelationId, attempt);

                // call FHIR API to get the resource
                var res = await _fhir.SendAsync(
                    FhirOperation.Get,
                    resourceType: ev.ResourceType,
                    resourceId: ev.TransactionId,
                    cancellationToken: ct);

                // Handle the two types of api responses:
                // { status:"success", code:200, message:..., data:{...FHIR...} }
                // { status:"error", code:404, message:"RESOURCE_NOT_FOUND", data:{ OperationOutcome... } }

                if (res.Code == 200 && string.Equals(res.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = TryMakeBsonPayload(res);

                    // ✅ Update the existing PENDING event to SUCCESS and attach payload
                    await _repo.MarkProbeSuccessAndAttachPayloadAsync(
                        ev.Id,
                        "Resource details fetched successfully (probe)",
                        payload,
                        ct);

                    _logger.LogInformation(
                        "✅ Probe success UPDATED existing StatusEvent for {ResourceType}/{ResourceId} (txn={Txn}).",
                        ev.ResourceType, ev.ResourceId, ev.TransactionId);
                    return;
                }

                if (res.Code == 404)
                {
                    // Spec: "for 404. do nothing afterward." → just stop probing
                    await _repo.MarkProbeSucceededAsync(ev.Id, ct);
                    _logger.LogWarning(
                        "🟡 Probe 404 RESOURCE_NOT_FOUND for {ResourceType}/{ResourceId} (txn={Txn}) – stopping.",
                        ev.ResourceType, ev.ResourceId, ev.TransactionId);
                    return;
                }

                // Other codes (429/5xx/etc.) → retry/backoff then abandon
                await RetryOrAbandonAsync(ev, $"HTTP {res.Code}: {res.Message}", attempt, ct);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                await RetryOrAbandonAsync(ev, ex.Message, attempt, ct, ex);
            }
        }

        private async Task RetryOrAbandonAsync(StatusEvent ev, string? error, int attempt, CancellationToken ct, Exception? ex = null)
        {
            var abandon = attempt >= _opt.MaxAttempts;
            var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * attempt)); // simple linear backoff capped @60s

            await _repo.MarkProbeRetryAsync(ev.Id, error, delay, abandon, ct);

            if (abandon)
            {
                if (ex != null)
                    _logger.LogError(ex, "⛔ Probe exception after {Attempts} for {ResourceType}/{ResourceId} (txn={Txn}). Abandoning.",
                        attempt, ev.ResourceType, ev.ResourceId, ev.TransactionId);
                else
                    _logger.LogError("⛔ Probe failed after {Attempts} (last error: {Error}) for {ResourceType}/{ResourceId} (txn={Txn}). Abandoning.",
                        attempt, error, ev.ResourceType, ev.ResourceId, ev.TransactionId);
            }
            else
            {
                if (ex != null)
                    _logger.LogWarning(ex, "♻️ Probe exception for {ResourceType}/{ResourceId} (txn={Txn}); retrying in {Delay}s (attempt {Attempt}/{Max}).",
                        ev.ResourceType, ev.ResourceId, ev.TransactionId, delay.TotalSeconds, attempt, _opt.MaxAttempts);
                else
                    _logger.LogWarning("♻️ Probe got error '{Error}' for {ResourceType}/{ResourceId} (txn={Txn}); retrying in {Delay}s (attempt {Attempt}/{Max}).",
                        error, ev.ResourceType, ev.ResourceId, ev.TransactionId, delay.TotalSeconds, attempt, _opt.MaxAttempts);
            }
        }

        /// <summary>
        /// Tries to capture a BSON document for StatusEvent.Data.
        /// Prefers a 'Raw' JSON string property if present on the response; falls back to 'Data' if present;
        /// otherwise serializes the entire response as a last resort.
        /// This keeps the worker self-contained without forcing changes to FhirApiResponse.
        /// </summary>
        private static BsonDocument? TryMakeBsonPayload(FhirApiResponse res)
        {
            try
            {
                // 1) Raw JSON (if the FhirApiService added it)
                var rawProp = res.GetType().GetProperty("Raw");
                if (rawProp != null)
                {
                    var raw = rawProp.GetValue(res) as string;
                    if (!string.IsNullOrWhiteSpace(raw))
                        return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(raw);
                }

                // 2) Data property (JsonElement or arbitrary object)
                var dataProp = res.GetType().GetProperty("Data");
                if (dataProp != null)
                {
                    var dataVal = dataProp.GetValue(res);
                    if (dataVal != null)
                    {
                        var json = JsonSerializer.Serialize(dataVal, new JsonSerializerOptions { WriteIndented = false });
                        return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(json);
                    }
                }

                // 3) Fallback: serialize whole response (ensures we persist *something*)
                var whole = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = false });
                return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(whole);
            }
            catch
            {
                return null;
            }
        }
    }

    public static class FhirApiResponseExtensions
    {
        public static BsonDocument? RawDocumentAsBson(this FhirApiResponse resp)
        {
            if (string.IsNullOrWhiteSpace(resp.Raw)) return null;
            try { return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(resp.Raw); }
            catch { return null; }
        }
    }
}
