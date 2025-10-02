using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{


    public class TokenService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthSettings _authSettings;
        private readonly ILogger<TokenService> _logger;
        private readonly AuthSettings _auth;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        public TokenService(HttpClient httpClient, IOptions<AuthSettings> authOptions, ILogger<TokenService> logger)
        {
            _httpClient = httpClient;
            _authSettings = authOptions.Value;
            _logger = logger;
            //if (_auth.TimeoutSeconds > 0) _httpClient.Timeout = TimeSpan.FromSeconds(_auth.TimeoutSeconds);


        }
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            var payloadObj = new
            {
                clientId = _authSettings.ClientId,
                clientSecret = _authSettings.ClientSecret,
                grantType = string.IsNullOrWhiteSpace(_authSettings.GrantType) ? "client_credentials" : _authSettings.GrantType,
                scope = _authSettings.Scope
            };

            var jsonPayload = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, _authSettings.TokenEndpoint)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _logger.LogInformation("🔐 Requesting access token: endpoint={Endpoint}, client_id={ClientId}, scope={Scope}",
                _authSettings.TokenEndpoint, Mask(_authSettings.ClientId), _authSettings.Scope);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("Token HTTP {Status}. Body (sanitized, truncated): {Body}",
                    (int)resp.StatusCode, Trunc(SanitizeSuccessBody(body), 600));
            }
            else
            {
                _logger.LogDebug("Token HTTP {Status}. Body (truncated): {Body}",
                    (int)resp.StatusCode, Trunc(body, 1000));
            }

            AuthTokenResponse? parsed = null;
            try { parsed = JsonSerializer.Deserialize<AuthTokenResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unable to parse token response JSON.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var summary = parsed is null
                    ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
                    : $"HTTP {(int)resp.StatusCode} ({parsed.Status}/{parsed.Code}) {parsed.Message}";
                throw new HttpRequestException($"Token request failed: {summary}");
            }

            if (parsed == null || !string.Equals(parsed.Status, "success", StringComparison.OrdinalIgnoreCase) || parsed.Code != 200)
            {
                var msg = parsed?.Message ?? "Non-success token payload";
                _logger.LogError("❌ Token payload error: status={Status}, code={Code}, message={Msg}",
                    parsed?.Status, parsed?.Code, msg);
                throw new InvalidOperationException($"Token payload not successful: {msg}");
            }

            var token = parsed.Data?.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogError("❌ access_token missing in successful response.");
                throw new InvalidOperationException("access_token missing in response.");
            }

            _logger.LogInformation("✅ Token acquired (token_type={Type}, expires_in={ExpiresIn}s)",
                parsed.Data?.TokenType, parsed.Data?.ExpiresIn);

            return token!;
        }

        private static string Mask(string? s, int head = 4, int tail = 2)
    => string.IsNullOrEmpty(s) ? "(null)"
       : s.Length <= head + tail ? new string('*', s.Length)
       : s[..head] + "…" + s[^tail..];

        private static string Trunc(string? s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

        // Remove/obscure access_token in success logs
        private static string SanitizeSuccessBody(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("access_token", out var _))
                {
                    // Rebuild with masked access_token to avoid leaking secrets in logs
                    var tokenType = data.TryGetProperty("token_type", out var tt) ? tt.GetString() : null;
                    var expiresIn = data.TryGetProperty("expires_in", out var ei) ? ei.GetRawText() : "null";
                    var scope = data.TryGetProperty("scope", out var sc) ? sc.GetString() : null;

                    return $"{{\"status\":\"success\",\"code\":200,\"message\":\"(omitted)\",\"data\":{{\"token_type\":\"{tokenType}\",\"expires_in\":{expiresIn},\"access_token\":\"***masked***\",\"scope\":\"{scope}\"}}}}";
                }
            }
            catch { /* best effort */ }

            return body;
        }
      
    }

}
