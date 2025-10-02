using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Infrastructure.Configuration;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    

    public sealed class AdminTokenService
    {
        private readonly HttpClient _http;
        private readonly ShipAdminAuthOptions _opts;
        private readonly ILogger<AdminTokenService> _log;

        // Simple in-memory cache
        private string? _cachedToken;
        private DateTimeOffset _expiresAt;
        private readonly object _gate = new();

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public AdminTokenService(IOptions<ShipAdminAuthOptions> opts, ILogger<AdminTokenService> log)
        {
            _opts = opts.Value ?? throw new ArgumentNullException(nameof(opts));
            _log = log;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default) 
        {
            // Return cached token if still valid
            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(_cachedToken) && _expiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                    return _cachedToken!;
            }

            var payload = new
            {
                clientId = _opts.ClientId,
                clientSecret = _opts.ClientSecret,
                grantType = string.IsNullOrWhiteSpace(_opts.GrantType) ? "client_credentials" : _opts.GrantType,
                scope = _opts.Scope
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.TokenUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null }),
                                            Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _log.LogInformation("🔐 Requesting Admin API token: endpoint={Endpoint}, clientId={ClientId}",
                _opts.TokenUrl, Mask(_opts.ClientId));  //

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("❌ Admin token request failed: HTTP {Code}. Body (trunc): {Body}",
                    (int)resp.StatusCode, Trunc(body, 800));
                throw new HttpRequestException($"Admin token HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Support common shapes:
                // 1) { "status":"success","code":200,"data":{"access_token":"...","expires_in":3600,"token_type":"Bearer"} }
                // 2) { "access_token":"...","expires_in":3600,"token_type":"Bearer" }
                // 3) { "token":"..." , "expiresIn": 3600 }

                string? token = null;
                int expiresIn = 3600;

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    token = GetString(data, "access_token") ?? GetString(data, "token");
                    expiresIn = GetInt(data, "expires_in") ?? GetInt(data, "expiresIn") ?? 3600;
                }
                else
                {
                    token = GetString(root, "access_token") ?? GetString(root, "token");
                    expiresIn = GetInt(root, "expires_in") ?? GetInt(root, "expiresIn") ?? 3600;
                }

                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Admin token response missing access_token/token.");

                lock (_gate)
                {
                    _cachedToken = token;
                    _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                }

                _log.LogInformation("✅ Admin token acquired (expires_in={ExpiresIn}s)", expiresIn);
                return token!;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "❌ Failed parsing admin token response.");
                throw;
            }

            static string? GetString(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            static int? GetInt(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        }

        private static string Mask(string? s, int head = 3, int tail = 2)
            => string.IsNullOrEmpty(s) ? "(null)" :
               s.Length <= head + tail ? new string('*', s.Length) : s[..head] + "…" + s[^tail..];

        private static string Trunc(string? s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
    }

}
