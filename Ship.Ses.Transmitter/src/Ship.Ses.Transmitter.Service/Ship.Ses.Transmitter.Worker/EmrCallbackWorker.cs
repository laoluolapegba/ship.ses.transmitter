using MongoDB.Bson;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public sealed class EmrCallbackWorker : BackgroundService
    {
        private readonly ILogger<EmrCallbackWorker> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        public EmrCallbackWorker(
            ILogger<EmrCallbackWorker> logger,
            IHttpClientFactory httpClientFactory,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🛰️ EMR Callback Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IMongoSyncRepository>();

                    // Fetch due jobs
                    var due = await repo.FetchDueEmrCallbacksAsync(batchSize: 50, stoppingToken);

                    foreach (var evt in due)
                    {
                        if (!await repo.TryMarkInFlightAsync(evt.Id, stoppingToken))
                            continue;

                        var targetUrl = await ResolveTargetUrlAsync(repo, evt, stoppingToken);
                        if (string.IsNullOrWhiteSpace(targetUrl))
                        {
                            await repo.MarkEmrCallbackRetryAsync(evt.Id, "Missing EMR callback URL",
                                delay: TimeSpan.FromMinutes(10), targetUrl: null, stoppingToken);
                            continue;
                        }

                        await SendToEmrAsync(repo, evt, targetUrl, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EMR Callback worker loop error");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            _logger.LogInformation("🛑 EMR Callback Worker stopped.");
        }

        private async Task<string?> ResolveTargetUrlAsync(IMongoSyncRepository repo, StatusEvent evt, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(evt.EmrTargetUrl))
                return evt.EmrTargetUrl;

            var patient = await repo.GetPatientByTransactionIdAsync(evt.TransactionId, ct);
            return patient?.ClientEMRCallbackUrl;
        }

        private async Task SendToEmrAsync(IMongoSyncRepository repo, StatusEvent evt, string targetUrl, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient("EmrCallback");
            var payload = BuildEmrPayload(evt);
            var json = payload.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                if ((int)resp.StatusCode is >= 200 and < 300)
                {
                    await repo.MarkEmrCallbackSucceededAsync(evt.Id, (int)resp.StatusCode, body, targetUrl, ct);
                    _logger.LogInformation("✅ EMR callback delivered (tx={Tx})", evt.TransactionId);
                }
                else
                {
                    await repo.MarkEmrCallbackRetryAsync(evt.Id, $"HTTP {(int)resp.StatusCode}: {Trim(body)}",
                        delay: Backoff(evt.CallbackAttempts), targetUrl, ct);
                    _logger.LogWarning("⚠️ EMR callback failed HTTP {Code} (tx={Tx})", (int)resp.StatusCode, evt.TransactionId);
                }
            }
            catch (Exception ex)
            {
                await repo.MarkEmrCallbackRetryAsync(evt.Id, ex.Message, Backoff(evt.CallbackAttempts), targetUrl, ct);
                _logger.LogError(ex, "❌ EMR callback exception (tx={Tx})", evt.TransactionId);
            }
        }

        private static JsonObject BuildEmrPayload(StatusEvent evt)
        {
            var obj = new JsonObject
            {
                ["status"] = evt.Status,
                ["message"] = evt.Message,
                ["shipId"] = evt.ShipId,
                ["transactionId"] = evt.TransactionId
            };
            if (evt.Data != null)
            {
                var json = evt.Data.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
                { OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson });
                obj["data"] = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            }
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
