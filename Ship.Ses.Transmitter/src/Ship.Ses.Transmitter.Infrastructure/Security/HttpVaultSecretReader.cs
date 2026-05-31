using System;
using System.Collections.Generic;
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
    /// Reads secrets from HashiCorp Vault's KV v2 engine over HTTP:
    /// <c>GET {Address}/v1/{KvMount}/data/{path}</c> with an <c>X-Vault-Token</c> header.
    /// Returns the inner <c>data.data</c> map, or <c>null</c> on 404.
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

            var requestUri = $"v1/{_opts.KvMount.Trim('/')}/data/{path.TrimStart('/')}";

            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!string.IsNullOrWhiteSpace(_opts.Token))
                req.Headers.TryAddWithoutValidation("X-Vault-Token", _opts.Token);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _log.LogWarning("🔐 Vault secret not found at {Path}", requestUri);
                return null;
            }

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Vault read failed for '{requestUri}': HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            // KV v2 shape: { "data": { "data": { ... }, "metadata": { ... } } }
            if (!doc.RootElement.TryGetProperty("data", out var outer) ||
                outer.ValueKind != JsonValueKind.Object ||
                !outer.TryGetProperty("data", out var inner) ||
                inner.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("🔐 Vault response for {Path} did not contain data.data", requestUri);
                return null;
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
    }
}
