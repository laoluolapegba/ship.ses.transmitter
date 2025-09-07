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
        private readonly IMongoSyncRepository _mongo;
        private readonly IHttpClientFactory _httpClientFactory;

        public EmrCallbackWorker(
            ILogger<EmrCallbackWorker> logger,
            IMongoSyncRepository mongo,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _mongo = mongo;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🛰️ EMR Callback Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var due = await _mongo.FetchDueEmrCallbacksAsync(batchSize: 50, stoppingToken);

                    foreach (var evt in due)
                    {
                        if (!await _mongo.TryMarkInFlightAsync(evt.Id, stoppingToken))
                            continue; // claimed by another instance

                        var targetUrl = await ResolveTargetUrlAsync(evt, stoppingToken);
                        if (string.IsNullOrWhiteSpace(targetUrl))
                        {
                            await _mongo.MarkEmrCallbackRetryAsync(evt.Id, "Missing EMR callback URL", TimeSpan.FromMinutes(10), null, stoppingToken);
                            continue;
                        }

                        var payload = BuildEmrPayload(evt); // same shape you received from SHIP
                        var json = payload.ToJsonString(new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = false
                        });

                        try
                        {
                            var client = _httpClientFactory.CreateClient("EmrCallback");
                            using var req = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                            {
                                Content = new StringContent(json, Encoding.UTF8, "application/json")
                            };

                            // Optional: add auth headers, correlation id, signatures here

                            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
                            var body = await resp.Content.ReadAsStringAsync(stoppingToken);

                            if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                            {
                                await _mongo.MarkEmrCallbackSucceededAsync(evt.Id, (int)resp.StatusCode, body, targetUrl, stoppingToken);
                                _logger.LogInformation("✅ EMR callback delivered (tx={Tx})", evt.TransactionId);
                            }
                            else
                            {
                                await _mongo.MarkEmrCallbackRetryAsync(evt.Id, $"HTTP {(int)resp.StatusCode}: {Trim(body)}", Backoff(evt.CallbackAttempts), targetUrl, stoppingToken);
                                _logger.LogWarning("⚠️ EMR callback failed HTTP {(Code)} (tx={Tx})", (int)resp.StatusCode, evt.TransactionId);
                            }
                        }
                        catch (Exception ex)
                        {
                            await _mongo.MarkEmrCallbackRetryAsync(evt.Id, ex.Message, Backoff(evt.CallbackAttempts), targetUrl, stoppingToken);
                            _logger.LogError(ex, "❌ EMR callback exception (tx={Tx})", evt.TransactionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EMR Callback worker loop error");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task<string?> ResolveTargetUrlAsync(StatusEvent evt, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(evt.EmrTargetUrl)) return evt.EmrTargetUrl;

            // Find patient record by transaction id (stored as SyncedResourceId)
            var patient = await _mongo.GetPatientByTransactionIdAsync(evt.TransactionId, ct);
            return patient?.ClientEMRCallbackUrl;
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
                // convert BsonDocument to JsonObject
                var json = evt.Data.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson });
                obj["data"] = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            }
            return obj;
        }

        private static TimeSpan Backoff(int attempts)
        {
            var n = Math.Clamp(attempts, 0, 10);
            var seconds = Math.Min(60 * 60, (int)Math.Pow(2, n)); // cap at 1h
            return TimeSpan.FromSeconds(Math.Max(30, seconds));   // min 30s
        }

        private static string Trim(string? s) => string.IsNullOrEmpty(s) ? "" : (s.Length <= 500 ? s : s[..500]);
    }

}
