using System;
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
    /// Resolves per-client outbound credentials from Vault, mirroring the Ingestor's model so DevOps
    /// configures Vault clients once. Vault is the source of truth for the client set: at startup
    /// (<see cref="InitializeAsync"/>) every client under the configured prefix is discovered and read,
    /// and only <b>active, non-revoked</b> clients with a usable secret are loaded into memory. There are
    /// no per-request Vault calls and no TTL; adding or rotating a client requires a restart. Non-secret
    /// material (token endpoint, grant type) comes from <see cref="AuthSettings"/>.
    /// </summary>
    public sealed class VaultClientCredentialProvider : IClientCredentialProvider
    {
        private static readonly string[] SecretKeyFallbacks = { "clientSecret", "client_secret", "hmac", "secret" };

        private readonly IVaultSecretReader _reader;
        private readonly VaultOptions _vault;
        private readonly AuthSettings _authDefaults;
        private readonly ILogger<VaultClientCredentialProvider> _log;

        private volatile IReadOnlyDictionary<string, ClientCredential> _clients =
            new Dictionary<string, ClientCredential>(StringComparer.Ordinal);

        public VaultClientCredentialProvider(
            IVaultSecretReader reader,
            IOptions<ClientCredentialsOptions> options,
            IOptions<AuthSettings> authDefaults,
            ILogger<VaultClientCredentialProvider> log)
        {
            _reader = reader;
            _vault = (options?.Value ?? throw new ArgumentNullException(nameof(options))).Vault;
            _authDefaults = authDefaults?.Value ?? throw new ArgumentNullException(nameof(authDefaults));
            _log = log;
        }

        /// <summary>Number of valid clients currently loaded in memory.</summary>
        public int Count => _clients.Count;

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            var prefix = _vault.ListPrefix();
            _log.LogInformation("🔐 Vault credential load: discovering clients at mount '{Mount}' (KV v{Kv}) under prefix '{Prefix}'…",
                _vault.KvMount, _vault.KvVersion, prefix);

            var clientIds = await _reader.ListClientIdsAsync(prefix, ct).ConfigureAwait(false);
            if (clientIds.Count == 0)
            {
                _log.LogWarning("🔐 Vault credential load found no registered clients under prefix '{Prefix}'. " +
                                "No outbound clients will be processed until a client is added and the worker restarts.", prefix);
                _clients = new Dictionary<string, ClientCredential>(StringComparer.Ordinal);
                return;
            }

            var loaded = new Dictionary<string, ClientCredential>(StringComparer.Ordinal);
            var skipped = 0;
            foreach (var clientId in clientIds)
            {
                var (cred, reason) = await TryLoadAsync(clientId, ct).ConfigureAwait(false);
                if (cred is null)
                {
                    skipped++;
                    _log.LogWarning("🔐 Vault credential load skipped client {ClientId}: {Reason}", clientId, reason);
                    continue;
                }
                loaded[clientId] = cred;
            }

            _clients = loaded;
            _log.LogInformation("🔐 Vault credential load complete: {Count} active client(s) loaded ({Skipped} skipped). Clients: {Clients}",
                loaded.Count, skipped, string.Join(", ", loaded.Keys));
        }

        public Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required.", nameof(clientId));

            if (_clients.TryGetValue(clientId, out var cred))
                return Task.FromResult(cred);

            throw new InvalidOperationException(
                $"No active Vault credential is loaded for client '{clientId}'. " +
                "It may be unregistered, inactive/revoked, or was added after startup (a restart is required to pick it up).");
        }

        public bool IsClientKnown(string clientId) =>
            !string.IsNullOrWhiteSpace(clientId) && _clients.ContainsKey(clientId);

        // Reads one client's secret and builds its credential, or returns a skip reason. A client is
        // loaded only when it is active, not revoked, and has a usable secret.
        private async Task<(ClientCredential? cred, string? reason)> TryLoadAsync(string clientId, CancellationToken ct)
        {
            IReadOnlyDictionary<string, string>? secret;
            try
            {
                secret = await _reader.ReadAsync(_vault.ResolvePath(clientId), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (null, $"secret read failed ({ex.Message})");
            }

            if (secret is null)
                return (null, "secret path not found");

            var clientSecret = ResolveSecret(secret);
            if (string.IsNullOrWhiteSpace(clientSecret))
                return (null, "no usable secret key");

            var isRevoked = ReadBool(secret, _vault.IsRevokedKey) ?? StatusIs(secret, "revoked");
            var isActive = ReadBool(secret, _vault.IsActiveKey) ?? !StatusIs(secret, "inactive");
            if (isRevoked) return (null, "client is revoked");
            if (!isActive) return (null, "client is inactive");

            var outboundClientId = (!string.IsNullOrWhiteSpace(_vault.ClientIdKey)
                                    && secret.TryGetValue(_vault.ClientIdKey, out var cid)
                                    && !string.IsNullOrWhiteSpace(cid))
                ? cid
                : clientId;

            var grant = string.IsNullOrWhiteSpace(_authDefaults.GrantType) ? "client_credentials" : _authDefaults.GrantType;
            return (new ClientCredential(_authDefaults.TokenEndpoint, outboundClientId, clientSecret!, grant), null);
        }

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

        private static bool? ReadBool(IReadOnlyDictionary<string, string> secret, string key)
            => !string.IsNullOrWhiteSpace(key) && secret.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : null;

        private bool StatusIs(IReadOnlyDictionary<string, string> secret, string expected)
            => !string.IsNullOrWhiteSpace(_vault.StatusKey)
               && secret.TryGetValue(_vault.StatusKey, out var status)
               && string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
    }
}
