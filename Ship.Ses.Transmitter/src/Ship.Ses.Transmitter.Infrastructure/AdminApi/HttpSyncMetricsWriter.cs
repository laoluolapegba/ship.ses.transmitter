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
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

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

            foreach (var group in metrics.GroupBy(m => m.ClientId))
            {
                var payload = new MetricsBulkRequestDto
                {
                    Items = group.Select(m => new SyncClientMetricWriteDto
                    {
                        ResourceType = m.ResourceType,
                        SyncWindowStart = m.SyncWindowStart,
                        SyncWindowEnd = m.SyncWindowEnd,
                        SyncedCount = m.SyncedCount,
                        FailedCount = m.FailedCount,
                        BatchId = m.BatchId,
                        Notes = m.Notes
                    }).ToList()
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, $"/clients/{group.Key}/metrics")
                {
                    Content = JsonContent.Create(payload)
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenService.GetAccessTokenAsync(ct));
                req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
                req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

                using var res = await _http.SendAsync(req, ct);
                res.EnsureSuccessStatusCode();
                _log.LogInformation("📊 Metrics posted: client={ClientId}, items={Count}", group.Key, payload.Items.Count);
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

            using var req = new HttpRequestMessage(HttpMethod.Put, $"/clients/{status.ClientId}/status")
            {
                Content = JsonContent.Create(dto)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenService.GetAccessTokenAsync(ct));
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
            req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            _log.LogInformation(" Status upserted for client={ClientId}", status.ClientId);
        }
    }
}
