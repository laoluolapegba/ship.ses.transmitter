using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// <see cref="IFhirTokenService"/> that caches outbound access tokens per (clientId, scope) with
    /// expiry, refresh-ahead and single-flight concurrency. Credentials are resolved per clientId via
    /// <see cref="IClientCredentialProvider"/>; scope is route-derived and supplied by the caller.
    /// Register as a singleton so the cache is shared process-wide.
    /// </summary>
    public sealed class CachedFhirTokenService : IFhirTokenService
    {
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;
        private readonly IClientCredentialProvider _credentials;
        private readonly ILogger<CachedFhirTokenService> _log;
        private readonly TimeProvider _clock;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

        public CachedFhirTokenService(
            IHttpClientFactory httpClientFactory,
            IClientCredentialProvider credentials,
            ILogger<CachedFhirTokenService> log,
            TimeProvider? timeProvider = null)
        {
            _http = httpClientFactory.CreateClient("FhirTokens");
            _credentials = credentials;
            _log = log;
            _clock = timeProvider ?? TimeProvider.System;
        }

        public async Task<string> GetAccessTokenAsync(string clientId, string? scope = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required.", nameof(clientId));

            var scopeKey = scope ?? string.Empty;
            var key = clientId + "|" + scopeKey;
            var entry = _cache.GetOrAdd(key, _ => new CacheEntry());

            if (entry.IsValid(_clock.GetUtcNow()))
                return entry.Token!;

            // Single-flight: only one caller refreshes a given (clientId, scope) at a time.
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (entry.IsValid(_clock.GetUtcNow()))
                    return entry.Token!;

                var cred = await _credentials.GetAsync(clientId, ct).ConfigureAwait(false);
                var (token, expiresIn) = await RequestTokenAsync(cred, scopeKey, ct).ConfigureAwait(false);

                entry.Token = token;
                entry.ExpiresAt = _clock.GetUtcNow().AddSeconds(expiresIn);

                _log.LogInformation("✅ Cached FHIR token for client={ClientId} scope={Scope} (expires_in={ExpiresIn}s)",
                    Mask(clientId), string.IsNullOrEmpty(scopeKey) ? "(none)" : scopeKey, expiresIn);

                return token;
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        private async Task<(string token, int expiresIn)> RequestTokenAsync(ClientCredential cred, string scope, CancellationToken ct)
        {
            var payload = new
            {
                clientId = cred.ClientId,
                clientSecret = cred.ClientSecret,
                grantType = string.IsNullOrWhiteSpace(cred.GrantType) ? "client_credentials" : cred.GrantType,
                scope = scope ?? string.Empty
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = null });

            using var req = new HttpRequestMessage(HttpMethod.Post, cred.TokenEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _log.LogInformation("🔐 Requesting FHIR token: endpoint={Endpoint}, client_id={ClientId}, scope={Scope}",
                cred.TokenEndpoint, Mask(cred.ClientId), string.IsNullOrEmpty(scope) ? "(none)" : scope);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            AuthTokenResponse? parsed = null;
            try { parsed = JsonSerializer.Deserialize<AuthTokenResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (Exception ex) { _log.LogError(ex, "❌ Unable to parse FHIR token response JSON."); }

            if (!resp.IsSuccessStatusCode)
            {
                var summary = parsed is null
                    ? $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
                    : $"HTTP {(int)resp.StatusCode} ({parsed.Status}/{parsed.Code}) {parsed.Message}";
                throw new HttpRequestException($"Token request failed: {summary}");
            }

            if (parsed == null || !string.Equals(parsed.Status, "success", StringComparison.OrdinalIgnoreCase) || parsed.Code != 200)
                throw new InvalidOperationException($"Token payload not successful: {parsed?.Message ?? "Non-success token payload"}");

            var token = parsed.Data?.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("access_token missing in response.");

            var expiresIn = parsed.Data!.ExpiresIn > 0 ? parsed.Data.ExpiresIn : 3600;
            return (token!, expiresIn);
        }

        private static string Mask(string? s, int head = 4, int tail = 2)
            => string.IsNullOrEmpty(s) ? "(null)"
               : s.Length <= head + tail ? new string('*', s.Length)
               : s[..head] + "…" + s[^tail..];

        private sealed class CacheEntry
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public string? Token;
            public DateTimeOffset ExpiresAt;

            public bool IsValid(DateTimeOffset now)
                => !string.IsNullOrEmpty(Token) && (ExpiresAt - RefreshMargin) > now;
        }
    }
}
