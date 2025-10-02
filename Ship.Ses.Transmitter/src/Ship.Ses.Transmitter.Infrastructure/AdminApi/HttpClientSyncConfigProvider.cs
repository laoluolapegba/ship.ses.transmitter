using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.AdminApi.Models;
using Ship.Ses.Transmitter.Infrastructure.Configuration;
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
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Org.BouncyCastle.Ocsp;
    using System.Diagnostics;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json;

    public sealed class HttpClientSyncConfigProvider : IClientSyncConfigProvider
    {
        private static readonly ActivitySource ActivitySource =
            new("Ship.Ses.Transmitter.SyncConfigProvider");

        private readonly HttpClient _http;
        private readonly AdminTokenService _tokenService;
        private readonly ShipAdminApiOptions _opts;
        private readonly ILogger<HttpClientSyncConfigProvider> _log;

        public bool _ignore; 

        public HttpClientSyncConfigProvider(
            IHttpClientFactory factory,
            AdminTokenService tokenService,
            IOptions<ShipAdminApiOptions> opts,
            ILogger<HttpClientSyncConfigProvider> log)
        {
            _http = factory.CreateClient("ShipAdminApi");
            _tokenService = tokenService;
            _opts = opts.Value;
            _log = log;
        }

        public async Task<bool> IsClientActiveAsync(string clientId)
        {
            using var activity = ActivitySource.StartActivity("SyncConfig.IsClientActive", ActivityKind.Client);
            activity?.SetTag("client.id", clientId);
            var cfg = await GetClientAsync(clientId, CancellationToken.None);
            var active = cfg?.IsActive ?? false;
            _log.LogInformation("Sync config check: IsClientActive={Active} for client {ClientId}", active, clientId);
            activity?.SetTag("result.active", active);
            return active;
        }

        public async Task<IEnumerable<string>> GetEnabledResourcesAsync(string clientId)
        {
            using var activity = ActivitySource.StartActivity("SyncConfig.GetEnabledResources", ActivityKind.Client);
            activity?.SetTag("client.id", clientId);
            var cfg = await GetClientAsync(clientId, CancellationToken.None);
            var resources = cfg?.EnabledResources ?? Enumerable.Empty<string>();
            _log.LogInformation("Sync config: {Count} enabled resources for client {ClientId}", resources.Count(), clientId);
            activity?.SetTag("result.resource_count", resources.Count());
            return resources;
        }

        public async Task<bool> IsClientValidAsync(string clientId)
        {
            using var activity = ActivitySource.StartActivity("SyncConfig.IsClientValid", ActivityKind.Client);
            activity?.SetTag("client.id", clientId);
            var exists = (await GetClientAsync(clientId, CancellationToken.None)) is not null;
            _log.LogInformation("Sync config check: IsClientValid={Exists} for client {ClientId}", exists, clientId);
            activity?.SetTag("result.exists", exists);
            return exists;
        }

        public Task<ClientConfigDto?> GetByFacilityAsync(string facilityId)
        {
            using var activity = ActivitySource.StartActivity("SyncConfig.GetByFacility", ActivityKind.Client);
            activity?.SetTag("facility.id", facilityId);
            return GetAsync<ClientConfigDto>($"/admin/clients/by-facility/{facilityId}", CancellationToken.None, treat404AsNull: true);
        }

        private Task<ClientConfigDto?> GetClientAsync(string clientId, CancellationToken ct)
            => GetAsync<ClientConfigDto>($"/admin/clients/{clientId}", ct, treat404AsNull: true);

        private async Task<T?> GetAsync<T>(string path, CancellationToken ct, bool treat404AsNull = false)
        {
            using var activity = ActivitySource.StartActivity("Http.Get ShipAdminApi", ActivityKind.Client);
            activity?.SetTag("http.method", "GET");
            activity?.SetTag("http.url", SafeUrl(_http.BaseAddress, path));

            var apiPrefix = "/api/v1";
            var fullPath = $"{apiPrefix.TrimEnd('/')}/{path.TrimStart('/')}";

            

            var sw = ValueStopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, fullPath);

                activity?.SetTag("http.url", SafeUrl(_http.BaseAddress, fullPath));

                // Auth (do not log token)
                var token = await _tokenService.GetAccessTokenAsync(ct);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                // Trace propagation
                var traceId = Activity.Current?.Id;
                if (!string.IsNullOrEmpty(traceId))
                    req.Headers.TryAddWithoutValidation("traceparent", traceId);

                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                activity?.SetTag("http.status_code", (int)res.StatusCode);

                // 404 → null (opt-in)
                if (treat404AsNull && res.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _log.LogInformation("↪ GET {Url} → 404 (treated as null) in {Elapsed}ms",
                        SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds());
                    return default;
                }

                // Non-success: read body safely for diagnostics, but don't crash
                if (!res.IsSuccessStatusCode)
                {
                    var errBody = await ReadSafeStringAsync(res.Content, ct);
                    _log.LogWarning("⚠️ GET {Url} → {Status} in {Elapsed}ms. Body (first 200): {Body}",
                        SafeUrl(_http.BaseAddress, path), (int)res.StatusCode, sw.GetElapsedMilliseconds(), Trim(errBody, 200));
                    // Option A: return default (soft-fail)
                    return default;
                    // Option B: throw controlled (uncomment if preferred)
                    // throw new InvalidOperationException($"Admin API returned {(int)res.StatusCode} for {path}");
                }

                // If content-type isn't JSON, don't try to parse
                if (!IsJsonContent(res.Content))
                {
                    var nonJson = await ReadSafeStringAsync(res.Content, ct);
                    _log.LogWarning("⚠️ GET {Url} returned non-JSON content-type in {Elapsed}ms. First 200 chars: {Body}",
                        SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds(), Trim(nonJson, 200));
                    return default;
                }

                // Parse JSON with protection
                try
                {
                    var payload = await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
                    var payloadJson = JsonSerializer.Serialize(payload);
                    _log.LogInformation("Received payload: {Payload}", payloadJson);

                    _log.LogInformation("✓ GET {Url} OK in {Elapsed}ms",
                        SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds());
                    return payload;
                }
                catch (JsonException jex)
                {
                    var raw = await ReadSafeStringAsync(res.Content, ct);
                    _log.LogError(jex, "✖ JSON parse error for {Url} in {Elapsed}ms. First 200 chars: {Body}",
                        SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds(), Trim(raw, 200));
                    return default; // soft-fail on bad JSON
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "canceled");
                _log.LogWarning("✖ GET {Url} canceled after {Elapsed}ms",
                    SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds());
                throw; // respect caller cancellation
            }
            catch (HttpRequestException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _log.LogError(ex, "✖ HTTP error calling {Url} after {Elapsed}ms",
                    SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds());
                return default; // soft-fail on transport errors
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _log.LogError(ex, "✖ Unexpected error calling {Url} after {Elapsed}ms",
                    SafeUrl(_http.BaseAddress, path), sw.GetElapsedMilliseconds());
                return default;
            }
            finally
            {
                activity?.SetTag("duration.ms", sw.GetElapsedMilliseconds());
            }
        }

        private static bool IsJsonContent(HttpContent content)
        {
            var ct = content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(ct)) return false;
            ct = ct.ToLowerInvariant();
            return ct.Contains("application/json") || ct.Contains("+json");
        }

        private static async Task<string> ReadSafeStringAsync(HttpContent content, CancellationToken ct)
        {
            try { return await content.ReadAsStringAsync(ct); }
            catch { return string.Empty; }
        }

        private static string Trim(string? s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);

        private static string SafeUrl(Uri? baseAddress, string path)
            => (baseAddress is null) ? path : new Uri(baseAddress, path).ToString();

        // stopwatch helper as before
        private readonly struct ValueStopwatch
        {
            private static readonly double ToMs = 1000.0 / Stopwatch.Frequency;
            private readonly long _start;
            private ValueStopwatch(long start) => _start = start;
            public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());
            public long GetElapsedMilliseconds() => (long)((Stopwatch.GetTimestamp() - _start) * ToMs);
        }
    }

}
