using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static Ship.Ses.Transmitter.Infrastructure.Configuration.ShipAdminApiOptions;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi
{
    public sealed class HttpHeartbeatSender : IHeartbeatSender
    {
        private readonly HttpClient _http;
        private readonly AdminTokenService _tokenService;
        private readonly HeartbeatOptions _opts;
        private readonly ILogger<HttpHeartbeatSender> _log;

        public HttpHeartbeatSender(
            IHttpClientFactory factory,
            AdminTokenService tokenService,
            IOptions<HeartbeatOptions> opts,
            ILogger<HttpHeartbeatSender> log)
        {
            _http = factory.CreateClient("ShipAdminApi");
            _tokenService = tokenService;
            _opts = opts.Value;
            _log = log;
        }

        public async Task SendAsync(string clientId, CancellationToken ct)
        {
            var path = _opts.PathTemplate.Replace("{clientId}", clientId, StringComparison.OrdinalIgnoreCase);

            using var req = new HttpRequestMessage(HttpMethod.Post, path)
            {
                // Empty POST; send truly empty body
                Content = new ByteArrayContent(Array.Empty<byte>())
            };

            var token = await _tokenService.GetAccessTokenAsync(ct);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
            req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

            try
            {
                using var res = await _http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);

                if (res.IsSuccessStatusCode)
                {
                    _log.LogDebug("🫀 Heartbeat OK → {Url} ({Status}). Body: {Body}",
                        new Uri(_http.BaseAddress!, path), (int)res.StatusCode, body);
                }
                else
                {
                    _log.LogWarning("🫀 Heartbeat failed → {Url} ({Status}). Body: {Body}",
                        new Uri(_http.BaseAddress!, path), (int)res.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // normal during shutdown
            }
            catch (Exception ex)
            {
                // Don’t explode the worker because heartbeat failed
                _log.LogWarning(ex, "🫀 Heartbeat error for {Url}", new Uri(_http.BaseAddress!, path));
            }
        }
    }
}
