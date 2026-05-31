using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Resolves per-client outbound credentials from Vault: the client secret/HMAC is read from
    /// <c>{KvMount}/data/{PathTemplate}</c> (keyed by clientId), while non-secret material
    /// (token endpoint, grant type) comes from <see cref="AuthSettings"/> defaults. Results are cached
    /// per clientId for <c>CacheTtlSeconds</c> so credential rotation is picked up within the TTL.
    /// </summary>
    public sealed class VaultClientCredentialProvider : IClientCredentialProvider
    {
        private static readonly string[] SecretKeyFallbacks = { "clientSecret", "client_secret", "hmac", "secret" };

        private readonly IVaultSecretReader _reader;
        private readonly VaultOptions _vault;
        private readonly AuthSettings _authDefaults;
        private readonly TimeProvider _clock;
        private readonly ILogger<VaultClientCredentialProvider> _log;
        private readonly ConcurrentDictionary<string, (ClientCredential cred, DateTimeOffset expiresAt)> _cache = new();

        public VaultClientCredentialProvider(
            IVaultSecretReader reader,
            IOptions<ClientCredentialsOptions> options,
            IOptions<AuthSettings> authDefaults,
            ILogger<VaultClientCredentialProvider> log,
            TimeProvider? timeProvider = null)
        {
            _reader = reader;
            _vault = (options?.Value ?? throw new ArgumentNullException(nameof(options))).Vault;
            _authDefaults = authDefaults?.Value ?? throw new ArgumentNullException(nameof(authDefaults));
            _log = log;
            _clock = timeProvider ?? TimeProvider.System;
        }

        public async Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required.", nameof(clientId));

            var now = _clock.GetUtcNow();
            if (_cache.TryGetValue(clientId, out var hit) && hit.expiresAt > now)
                return hit.cred;

            var path = _vault.PathTemplate.Replace("{clientId}", clientId, StringComparison.OrdinalIgnoreCase);
            var secret = await _reader.ReadAsync(path, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No Vault secret found for client '{clientId}' at '{path}'.");

            var clientSecret = ResolveSecret(secret)
                ?? throw new InvalidOperationException($"Vault secret at '{path}' has no usable secret key for client '{clientId}'.");

            var outboundClientId = (!string.IsNullOrWhiteSpace(_vault.ClientIdKey)
                                    && secret.TryGetValue(_vault.ClientIdKey, out var cid)
                                    && !string.IsNullOrWhiteSpace(cid))
                ? cid
                : clientId;

            var grant = string.IsNullOrWhiteSpace(_authDefaults.GrantType) ? "client_credentials" : _authDefaults.GrantType;
            var cred = new ClientCredential(_authDefaults.TokenEndpoint, outboundClientId, clientSecret, grant);

            var ttl = TimeSpan.FromSeconds(_vault.CacheTtlSeconds > 0 ? _vault.CacheTtlSeconds : 300);
            _cache[clientId] = (cred, now.Add(ttl));

            _log.LogInformation("🔐 Resolved Vault credential for client {ClientId} (cached {Ttl}s).", clientId, (int)ttl.TotalSeconds);
            return cred;
        }

        /// <summary>Drops the cached credential for a client (e.g. after an auth failure / known rotation).</summary>
        public void Invalidate(string clientId) => _cache.TryRemove(clientId, out _);

        private string? ResolveSecret(IReadOnlyDictionary<string, string> secret)
        {
            if (!string.IsNullOrWhiteSpace(_vault.SecretKey) &&
                secret.TryGetValue(_vault.SecretKey, out var configured) && !string.IsNullOrWhiteSpace(configured))
                return configured;

            foreach (var key in SecretKeyFallbacks)
                if (secret.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;

            return null;
        }
    }
}
