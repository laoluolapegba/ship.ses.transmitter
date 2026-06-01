using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Reads secrets from HashiCorp Vault's KV engine over HTTP. For KV v2:
    /// <c>GET {Address}/v1/{KvMount}/data/{path}</c> to read and
    /// <c>GET {Address}/v1/{KvMount}/metadata/{prefix}?list=true</c> to discover clients.
    /// For KV v1 the <c>data</c>/<c>metadata</c> segments are omitted. An <c>X-Vault-Token</c> header is sent.
    /// </summary>
    public sealed class HttpVaultSecretReader : IVaultSecretReader
    {
        private readonly HttpClient _http;
        private readonly VaultOptions _opts;
        private readonly ILogger<HttpVaultSecretReader> _log;

        public HttpVaultSecretReader(
            IHttpClientFactory httpClientFactory,
            IOptions<ClientCredentialsOptions> opts,
            ILogger<HttpVaultSecretReader> log)
        {
            _http = httpClientFactory.CreateClient("Vault");
            _opts = (opts?.Value ?? throw new ArgumentNullException(nameof(opts))).Vault;
            _log = log;
        }

        public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Secret path is required.", nameof(path));

            var mount = _opts.KvMount.Trim('/');
            var rel = path.TrimStart('/');
            var requestUri = _opts.KvVersion == 2 ? $"v1/{mount}/data/{rel}" : $"v1/{mount}/{rel}";

            using var resp = await SendAsync(requestUri, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogWarning("🔐 Vault secret not found at {Path}", requestUri);
                return null;
            }
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Vault read failed for '{requestUri}': HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            // KV v2 shape: { "data": { "data": { ... }, "metadata": { ... } } }; KV v1: { "data": { ... } }
            if (!doc.RootElement.TryGetProperty("data", out var outer) || outer.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("🔐 Vault response for {Path} did not contain data", requestUri);
                return null;
            }

            var inner = outer;
            if (_opts.KvVersion == 2)
            {
                if (!outer.TryGetProperty("data", out inner) || inner.ValueKind != JsonValueKind.Object)
                {
                    _log.LogWarning("🔐 Vault response for {Path} did not contain data.data", requestUri);
                    return null;
                }
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in inner.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.GetRawText();
            }
            return result;
        }

        public async Task<IReadOnlyList<string>> ListClientIdsAsync(string listPrefix, CancellationToken ct = default)
        {
            var mount = _opts.KvMount.Trim('/');
            var prefix = (listPrefix ?? string.Empty).Trim('/');
            var requestUri = _opts.KvVersion == 2 ? $"v1/{mount}/metadata/{prefix}" : $"v1/{mount}/{prefix}";

            using var resp = await SendAsync($"{requestUri}?list=true", ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogWarning("🔐 Vault list prefix '{Prefix}' not found.", requestUri);
                return Array.Empty<string>();
            }
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                _log.LogWarning("🔐 Vault list denied for '{Prefix}'. The token requires 'list' capability.", requestUri);
                return Array.Empty<string>();
            }
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Vault list failed for '{requestUri}': HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            // { "data": { "keys": [ "emr-a", "emr-b/" ] } }
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return keys.EnumerateArray()
                .Where(k => k.ValueKind == JsonValueKind.String)
                .Select(k => k.GetString()!.TrimEnd('/'))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }

        private async Task<HttpResponseMessage> SendAsync(string requestUri, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(_opts.Token))
                req.Headers.TryAddWithoutValidation("X-Vault-Token", _opts.Token);
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
    }
}
