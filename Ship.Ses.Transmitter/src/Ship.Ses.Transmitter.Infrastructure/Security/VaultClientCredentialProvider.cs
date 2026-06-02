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
    /// Resolves per-client outbound credentials from Vault, mirroring the SeS Ingestor so DevOps configures
    /// Vault clients once. Vault is the source of truth for the client set: at startup
    /// (<see cref="InitializeAsync"/>) every client under the configured prefix is discovered and read, and
    /// only <b>active, non-revoked</b> clients with a usable secret are loaded into memory. There are no
    /// per-request Vault calls and no TTL; adding or rotating a client requires a restart. Connection
    /// settings come from OS environment variables (<see cref="VaultClientSecretSettings"/>); non-secret
    /// material (token endpoint, grant type) comes from <see cref="AuthSettings"/>. The Vault folder name is
    /// the clientId; the outbound <c>client_id</c> is that same value (no override key).
    /// </summary>
    public sealed class VaultClientCredentialProvider : IClientCredentialProvider
    {
        private readonly IVaultSecretReader _reader;
        private readonly VaultClientSecretSettings _vault;
        private readonly AuthSettings _authDefaults;
        private readonly ILogger<VaultClientCredentialProvider> _log;

        private volatile IReadOnlyDictionary<string, ClientCredential> _clients =
            new Dictionary<string, ClientCredential>(StringComparer.Ordinal);

        public VaultClientCredentialProvider(
            IVaultSecretReader reader,
            VaultClientSecretSettings settings,
            IOptions<AuthSettings> authDefaults,
            ILogger<VaultClientCredentialProvider> log)
        {
            _reader = reader;
            _vault = settings ?? throw new ArgumentNullException(nameof(settings));
            _authDefaults = authDefaults?.Value ?? throw new ArgumentNullException(nameof(authDefaults));
            _log = log;
        }

        /// <summary>Number of valid clients currently loaded in memory.</summary>
        public int Count => _clients.Count;

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            // Fail fast: Vault is mandatory and configured via OS env vars (VAULT_ADDR / VAULT_TOKEN).
            if (!_vault.IsConfigured)
                throw new InvalidOperationException(
                    "Vault is not configured: set the VAULT_ADDR and VAULT_TOKEN environment variables. " +
                    "Per-client outbound credentials are loaded from Vault at startup.");

            var prefix = _vault.ListPrefix();
            _log.LogInformation("🔐 Vault credential load: discovering clients at {Address} (mount '{Mount}', KV v{Kv}) under prefix '{Prefix}'…",
                _vault.Address, _vault.Mount, _vault.KvVersion, prefix);

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
            _log.LogInformation("🔐 Vault credential load complete: {Loaded} active client(s) loaded, {Skipped} skipped (of {Discovered} discovered). Loaded: {Clients}",
                loaded.Count, skipped, clientIds.Count, string.Join(", ", loaded.Keys));
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
        // loaded only when it is active, not revoked, and has a usable secret. The folder name is the clientId.
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

            if (!secret.TryGetValue(_vault.SecretKey, out var clientSecret) || string.IsNullOrWhiteSpace(clientSecret))
                return (null, $"secret field '{_vault.SecretKey}' is missing or empty");

            var isRevoked = ReadBool(secret, _vault.IsRevokedKey) ?? StatusIs(secret, "revoked");
            var isActive = ReadBool(secret, _vault.IsActiveKey) ?? !StatusIs(secret, "inactive");
            if (isRevoked) return (null, "client is revoked");
            if (!isActive) return (null, "client is inactive");

            var grant = string.IsNullOrWhiteSpace(_authDefaults.GrantType) ? "client_credentials" : _authDefaults.GrantType;
            return (new ClientCredential(_authDefaults.TokenEndpoint, clientId, clientSecret, grant), null);
        }

        private static bool? ReadBool(IReadOnlyDictionary<string, string> secret, string key)
            => !string.IsNullOrWhiteSpace(key) && secret.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : null;

        private bool StatusIs(IReadOnlyDictionary<string, string> secret, string expected)
            => !string.IsNullOrWhiteSpace(_vault.StatusKey)
               && secret.TryGetValue(_vault.StatusKey, out var status)
               && string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
    }
}
