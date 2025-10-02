using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.AdminApi.Models;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Dynamic.Core.Tokenizer;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi
{
    public sealed class HttpSyncMetricsWriter : ISyncMetricsWriter
    {
        private readonly HttpClient _http;
        private readonly AdminTokenService _tokenService;
        private readonly ShipAdminApiOptions _opts;
        private readonly ILogger<HttpSyncMetricsWriter> _log;

        public HttpSyncMetricsWriter(
            IHttpClientFactory factory,
            AdminTokenService tokenService,
            IOptions<ShipAdminApiOptions> opts,
            ILogger<HttpSyncMetricsWriter> log)
        {
            _http = factory.CreateClient("ShipAdminApi");
            _tokenService = tokenService;
            _opts = opts.Value;
            _log = log;
        }

        public Task WriteMetricAsync(SyncClientMetric metric)
            => WriteMetricsAsync(new[] { metric });

        public async Task WriteMetricsAsync(IEnumerable<SyncClientMetric> metrics)
        {
            var ct = CancellationToken.None;

            // Use a class that mirrors the new payload structure
    

            foreach (var group in metrics.GroupBy(m => m.ClientId))
            {
                var payload = new MetricsPayloadDto
                {
                    Items = group.Select(m => new MetricItemDto
                    {
                        ResourceType = m.ResourceType,
                        WindowStart = m.SyncWindowStart,
                        WindowEnd = m.SyncWindowEnd,
                        CountSynced = m.SyncedCount,
                        CountFailed = m.FailedCount
                    }).ToList()
                };

                // Construct the correct URL path
                var path = $"/api/v1/metrics/{group.Key}/client";

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                // Log the outgoing request
                _log.LogInformation("➡️ POSTing {Count} metrics to {Url}. Payload: {Payload}",
                    payload.Items.Count, new Uri(_http.BaseAddress, path).ToString(), JsonSerializer.Serialize(payload, jsonOptions));

                using var req = new HttpRequestMessage(HttpMethod.Post, path)
                      {
                          // Use the configured options to create the JSON content
                          Content = JsonContent.Create(payload, options: jsonOptions)
                      };

                // Get and log the access token for debugging
                var token = await _tokenService.GetAccessTokenAsync(ct);
                _log.LogInformation("🔐 Using access token: {Token}",
                    string.IsNullOrEmpty(token) ? "Token not found" : $"{token.Substring(0,10)}...");

                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
                req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

                try
                {
                    using var res = await _http.SendAsync(req, ct);
                    var responseBody = await res.Content.ReadAsStringAsync(ct);
            
                    if (res.IsSuccessStatusCode)
                    {
                        _log.LogInformation("✅ POST to {Url} succeeded with status {StatusCode}. Response: {ResponseBody}",
                            new Uri(_http.BaseAddress, path).ToString(), (int) res.StatusCode, responseBody);
                }
                    else
                    {
                        _log.LogError("❌ POST to {Url} failed with status {StatusCode}. Response: {ResponseBody}",
                            new Uri(_http.BaseAddress, path).ToString(), (int) res.StatusCode, responseBody);
                        res.EnsureSuccessStatusCode(); // Throws if not successful
                    }
                }
                catch (HttpRequestException ex)
                {
                    _log.LogError(ex, "✖ HTTP request error for {Url}", new Uri(_http.BaseAddress, path).ToString());
                    throw;
                }
            }
        }

        public async Task WriteStatusAsync(SyncClientStatus status)
        {
            var ct = CancellationToken.None;

            var dto = new SyncClientStatusWriteDto
            {
                Status = status.Status,
                LastCheckIn = status.LastCheckIn ?? DateTime.UtcNow,
                LastSyncedAt = status.LastSyncedAt,
                TotalSynced = status.TotalSynced,
                TotalFailed = status.TotalFailed,
                CurrentBatchId = status.CurrentBatchId,
                LastError = status.LastError,
                IpAddress = status.IpAddress,
                Hostname = status.Hostname,
                Version = status.Version,
                SignatureHash = status.SignatureHash
            };

            var path = $"/api/v1/status/{status.ClientId}/client";

            // Configure JsonSerializer to use camelCase
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            _log.LogInformation("➡️ PUTting status to {Url}. Payload: {Payload}",
                new Uri(_http.BaseAddress, path).ToString(), JsonSerializer.Serialize(dto, jsonOptions));

            using var req = new HttpRequestMessage(HttpMethod.Put, path)
            {
                // Use the configured options to create the JSON content
                Content = JsonContent.Create(dto, options: jsonOptions)
            };

            // Get and log the access token for debugging
            var token = await _tokenService.GetAccessTokenAsync(ct);
            _log.LogInformation("🔐 Using access token: {Token}",
                string.IsNullOrEmpty(token) ? "Token not found" : $"{token.Substring(0,10)} ...");

            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
            req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

            try
            {
                using var res = await _http.SendAsync(req, ct);
                var responseBody = await res.Content.ReadAsStringAsync(ct);

                if (res.IsSuccessStatusCode)
                {
                    _log.LogInformation("✅ PUT to {Url} succeeded with status {StatusCode}. Response: {ResponseBody}",
                        new Uri(_http.BaseAddress, path).ToString(), (int)res.StatusCode, responseBody);
                }
                else
                {
                    _log.LogError("❌ PUT to {Url} failed with status {StatusCode}. Response: {ResponseBody}",
                        new Uri(_http.BaseAddress, path).ToString(), (int)res.StatusCode, responseBody);
                    res.EnsureSuccessStatusCode(); // Throws if not successful
                }
            }
            catch (HttpRequestException ex)
            {
                _log.LogError(ex, "✖ HTTP request error for {Url}", new Uri(_http.BaseAddress, path).ToString());
                throw;
            }
        }
    }
    public class MetricsPayloadDto
    {
        public List<MetricItemDto> Items { get; set; } = new List<MetricItemDto>();
    }

    public class MetricItemDto
    {
        public string ResourceType { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public int CountSynced { get; set; }
        public int CountFailed { get; set; }
    }
}
