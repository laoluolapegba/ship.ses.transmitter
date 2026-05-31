using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    /// <summary>
    /// This worker processes EMR callbacks by polling the database for due status events,
    /// SHIP status events with pending EMR callbacks, and attempts to deliver them
    /// The events are gotten from the StatusEvents collection. and initially received from SHIP by the ingestor service.
    /// The events are sent to the EMR callback URL specified in the patient/general resources record.
    /// The events are marked as InFlight to avoid duplicate processing.
    /// The events are retried with exponential backoff on failure.
    /// The events are marked as succeeded on successful delivery.
    /// The worker runs continuously until the application is stopped.
    /// </summary>
    public sealed class EmrCallbackWorker : BackgroundService
    {
        private readonly ILogger<EmrCallbackWorker> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICallbackUrlValidator _validator;
        private readonly int _maxAttempts;

        public EmrCallbackWorker(
            ILogger<EmrCallbackWorker> logger,
            IHttpClientFactory httpClientFactory,
            IServiceScopeFactory scopeFactory,
            ICallbackUrlValidator validator,
            IOptions<EmrCallbackOptions> options)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _validator = validator;
            _maxAttempts = Math.Max(1, options.Value?.MaxAttempts ?? 8);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            const int batchSize = 50;
            var pollDelay = TimeSpan.FromSeconds(10);

            _logger.LogInformation("🛰️ EMR Callback Worker started (batchSize={Batch}, pollDelay={Delay}s)",
                batchSize, pollDelay.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IFhirSyncStore>();

                    _logger.LogInformation("🔎 Polling for due EMR callbacks…");

                    // Fetch due jobs
                    var due = await repo.FetchDueEmrCallbacksAsync(batchSize: batchSize, stoppingToken);
                    var dueList = due?.ToList() ?? new List<StatusEvent>();
                    sw.Stop();

                    _logger.LogInformation("📥 Poll complete: found={Count}, elapsedMs={Elapsed}",
                        dueList.Count, sw.ElapsedMilliseconds);

                    if (dueList.Count == 0)
                    {
                        _logger.LogDebug("🕰️ No callbacks due. Sleeping for {Delay}s…", pollDelay.TotalSeconds);
                        await Task.Delay(pollDelay, stoppingToken);
                        continue;
                    }

                    foreach (var evt in dueList)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        // Mark in-flight (skip if someone else took it)
                        if (!await repo.TryClaimEmrCallbackAsync(evt.Id, stoppingToken))
                        {
                            _logger.LogDebug("⏭️ Skipped: could not mark in-flight (id={Id}, tx={Tx}, corr={Corr})",
                                evt.Id, evt.TransactionId, evt.CorrelationId);
                            continue;
                        }

                        // Resolve target URL
                        var targetUrl = await ResolveTargetUrlAsync(repo, evt, stoppingToken);

                        // Validate the (stored) callback URL before calling out (SSRF guard, opt-in — Finding 5.1).
                        if (!_validator.IsAllowed(targetUrl, evt.ClientId, out var rejectReason))
                        {
                            _logger.LogWarning("⛔ Callback URL rejected (tx={Tx}, corr={Corr}, client={Client}): {Reason}",
                                evt.TransactionId, evt.CorrelationId, evt.ClientId, rejectReason);

                            // A missing URL may be backfilled later → retry up to MaxAttempts; a present but
                            // disallowed URL will never become valid → dead-letter immediately.
                            if (string.IsNullOrWhiteSpace(targetUrl) && evt.CallbackAttempts + 1 < _maxAttempts)
                                await repo.MarkEmrCallbackRetryAsync(evt.Id, rejectReason, TimeSpan.FromMinutes(10), null, stoppingToken);
                            else
                                await repo.MarkEmrCallbackFailedAsync(evt.Id, rejectReason, targetUrl, stoppingToken);

                            continue;
                        }

                        _logger.LogInformation("🚚 Dispatching EMR callback (tx={Tx}, corr={Corr}) → {Url}",
                            evt.TransactionId, evt.CorrelationId, SafeUrl(targetUrl!));

                        await SendToEmrAsync(repo, evt, targetUrl!, stoppingToken);
                    }
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "💥 EMR Callback worker loop error after {Elapsed}ms", sw.ElapsedMilliseconds);
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("⏲️ Sleeping for {Delay}s before next poll…", pollDelay.TotalSeconds);
                    await Task.Delay(pollDelay, stoppingToken);
                }
            }

            _logger.LogInformation("🛑 EMR Callback Worker stopped.");
        }


        private async Task<string?> ResolveTargetUrlAsync(IFhirSyncStore repo, StatusEvent evt, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(evt.EmrTargetUrl))
                return evt.EmrTargetUrl;

            var patient = await repo.GetPatientByTransactionIdAsync(evt.TransactionId, ct);
            return patient?.ClientEMRCallbackUrl;
        }

        private async Task SendToEmrAsync(IFhirSyncStore repo, StatusEvent evt, string targetUrl, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient("EmrCallback");
            var payload = BuildEmrPayload(evt);
            var json = payload.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // ── Logging (what we're sending)
            _logger.LogInformation(
                "➡️ Sending EMR callback to {Url} (tx={Tx}, corr={Corr}, status={Status})",
                SafeUrl(targetUrl), evt.TransactionId, evt.CorrelationId, evt.Status);

            _logger.LogDebug(
                "EMR callback payload (tx={Tx}, corr={Corr}): {Payload}",
                evt.TransactionId, evt.CorrelationId, Trim(json, 2000)); // cap to 2KB

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                //  correlation headers for the EMR
                req.Headers.TryAddWithoutValidation("x-transaction-id", evt.TransactionId);
                if (!string.IsNullOrWhiteSpace(evt.ClientId))
                    req.Headers.TryAddWithoutValidation("x-client-id", evt.ClientId);
                if (!string.IsNullOrWhiteSpace(evt.CorrelationId))
                    req.Headers.TryAddWithoutValidation("x-correlation-id", evt.CorrelationId);
                if (!string.IsNullOrWhiteSpace(evt.ResourceType))
                    req.Headers.TryAddWithoutValidation("x-fhir-resource-type", evt.ResourceType);
                if (!string.IsNullOrWhiteSpace(evt.ResourceId))
                    req.Headers.TryAddWithoutValidation("x-fhir-resource-id", evt.ResourceId);

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if ((int)resp.StatusCode is >= 200 and < 300)
                {
                    await repo.MarkEmrCallbackSucceededAsync(evt.Id, (int)resp.StatusCode, body, targetUrl, ct);
                    _logger.LogInformation("✅ EMR callback delivered (tx={Tx}, corr={Corr})", evt.TransactionId, evt.CorrelationId);
                }
                else
                {
                    await RetryOrDeadLetterAsync(repo, evt, $"HTTP {(int)resp.StatusCode}: {Trim(body)}", targetUrl, ct);

                    _logger.LogWarning(
                        "⚠️ EMR callback failed HTTP {Code} (tx={Tx}, corr={Corr})",
                        (int)resp.StatusCode, evt.TransactionId, evt.CorrelationId);
                }
            }
            catch (Exception ex)
            {
                await RetryOrDeadLetterAsync(repo, evt, ex.Message, targetUrl, ct);
                _logger.LogError(ex, "❌ EMR callback exception (tx={Tx}, corr={Corr})", evt.TransactionId, evt.CorrelationId);
            }
        }

        // Retry with backoff, or dead-letter once MaxAttempts is reached (Finding 5.4).
        private Task RetryOrDeadLetterAsync(IFhirSyncStore repo, StatusEvent evt, string? error, string targetUrl, CancellationToken ct)
        {
            if (evt.CallbackAttempts + 1 >= _maxAttempts)
            {
                _logger.LogError("⛔ EMR callback dead-lettered after {Attempts} attempt(s) (tx={Tx}, corr={Corr}): {Error}",
                    evt.CallbackAttempts + 1, evt.TransactionId, evt.CorrelationId, error);
                return repo.MarkEmrCallbackFailedAsync(evt.Id, error, targetUrl, ct);
            }

            return repo.MarkEmrCallbackRetryAsync(evt.Id, error, Backoff(evt.CallbackAttempts), targetUrl, ct);
        }

        private static string SafeUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                var safe = new UriBuilder(u) { Query = string.Empty, Fragment = string.Empty };
                return safe.Uri.ToString();
            }
            catch
            {
                return "(invalid-url)";
            }
        }

        private static string Trim(string? s, int max = 500) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);


        private static JsonObject BuildEmrPayload(StatusEvent evt)
        {
            var obj = new JsonObject
            {
                ["status"] = evt.Status,
                ["message"] = evt.Message,
                ["shipId"] = evt.ShipId,
                ["transactionId"] = evt.TransactionId,
                ["correlationId"] = evt.CorrelationId
            };

            //if (evt.Data != null)
            //{
            //    var json = evt.Data.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
            //    { OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson });
            //    obj["data"] = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            //}
            return obj;
        }

        private static TimeSpan Backoff(int attempts)
        {
            var n = Math.Clamp(attempts, 0, 10);
            var seconds = Math.Min(3600, (int)Math.Pow(2, n)); // cap 1h
            return TimeSpan.FromSeconds(Math.Max(30, seconds)); // min 30s
        }

        private static string Trim(string? s) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= 500 ? s : s[..500]);
    }


}
