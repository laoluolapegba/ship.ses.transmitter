using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi
{
    
    public sealed class HttpHeartbeatClient : IHeartbeatClient
    {
        private readonly HttpClient _http;
        private readonly AdminTokenService _tokenService;
        private readonly ShipAdminApiOptions _opts;
        private readonly ILogger<HttpHeartbeatClient> _log;

        public HttpHeartbeatClient(
            IHttpClientFactory factory,
            AdminTokenService tokenService,
            IOptions<ShipAdminApiOptions> opts,
            ILogger<HttpHeartbeatClient> log)
        {
            _http = factory.CreateClient("ShipAdminApi");
            _tokenService = tokenService;
            _opts = opts.Value;
            _log = log;
        }

        public async Task<DateTime?> SendAsync(string clientId, CancellationToken ct)
        {
            var template = _opts.Heartbeat?.PathTemplate
                           ?? "/api/v1/status/{clientId}/client/heartbeat";

            var path = template.Contains("{clientId}", StringComparison.OrdinalIgnoreCase)
                ? template.Replace("{clientId}", clientId, StringComparison.OrdinalIgnoreCase)
                : template;

            using var req = new HttpRequestMessage(HttpMethod.Post, path)
            {
                // truly empty POST body to match your requirement
                Content = new ByteArrayContent(Array.Empty<byte>())
            };

            var token = await _tokenService.GetAccessTokenAsync(ct);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", Idempotency.NewKey());
            req.Headers.TryAddWithoutValidation("Traceparent", System.Diagnostics.Activity.Current?.Id);

            var fullUrl = new Uri(_http.BaseAddress!, path).ToString();
            _log.LogInformation("🫀 Sending heartbeat → {Url}", fullUrl);

            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("💔 Heartbeat failed ({Status}): {Body}", (int)res.StatusCode, body);
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var ts = doc.RootElement.GetProperty("timestamp").GetDateTime();
                _log.LogInformation("✅ Heartbeat ack: {Timestamp} from {Url}", ts, fullUrl);
                return ts;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Heartbeat response parse error. Raw: {Body}", body);
                return null;
            }
        }
    }

}
