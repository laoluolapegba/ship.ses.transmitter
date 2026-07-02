using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Resolves per-client outbound credentials from configuration (<c>AppSettings:Clients</c>). Secret
    /// values are injected into the runtime as environment variables before startup by the organization's
    /// ISW secret-injection mechanism (a HashiCorp Vault agent) and bound over the committed placeholders by
    /// the environment-variable configuration provider. The application therefore performs <b>no</b> Vault
    /// list/read API calls, knows no Vault address or token, and handles no <c>X-Vault-Token</c>.
    /// <para>
    /// At startup (<see cref="InitializeAsync"/>) every <c>ACTIVE</c> client with a usable secret is loaded
    /// into memory. There are no per-request lookups and no TTL; adding, removing or rotating a client
    /// requires a restart. Non-secret material (token endpoint, grant type) comes from <see cref="AuthSettings"/>;
    /// the per-client <c>ClientSecret</c>/<c>HmacSecret</c> come from the client entry.
    /// </para>
    /// </summary>
    public sealed class ConfigClientCredentialProvider : IClientCredentialProvider
    {
        private readonly ClientDirectoryOptions _directory;
        private readonly AuthSettings _authDefaults;
        private readonly ILogger<ConfigClientCredentialProvider> _log;

        private volatile IReadOnlyDictionary<string, ClientCredential> _clients =
            new Dictionary<string, ClientCredential>(StringComparer.Ordinal);

        public ConfigClientCredentialProvider(
            IOptions<ClientDirectoryOptions> directory,
            IOptions<AuthSettings> authDefaults,
            ILogger<ConfigClientCredentialProvider> log)
        {
            _directory = directory?.Value ?? throw new ArgumentNullException(nameof(directory));
            _authDefaults = authDefaults?.Value ?? throw new ArgumentNullException(nameof(authDefaults));
            _log = log;
        }

        /// <summary>Number of valid clients currently loaded in memory.</summary>
        public int Count => _clients.Count;

        public Task InitializeAsync(CancellationToken ct = default)
        {
            var grant = string.IsNullOrWhiteSpace(_authDefaults.GrantType) ? "client_credentials" : _authDefaults.GrantType;
            var loaded = new Dictionary<string, ClientCredential>(StringComparer.Ordinal);
            var skipped = 0;

            foreach (var entry in _directory.Clients ?? Enumerable.Empty<ClientCredentialEntry>())
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.ClientId))
                {
                    skipped++;
                    _log.LogWarning("🔐 Skipped a client entry with no ClientId.");
                    continue;
                }

                if (!entry.IsActive)
                {
                    skipped++;
                    _log.LogWarning("🔐 Skipped client {ClientId}: Status='{Status}' (not ACTIVE).", entry.ClientId, entry.Status);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ClientSecret))
                {
                    skipped++;
                    _log.LogWarning("🔐 Skipped client {ClientId}: ClientSecret is missing or empty " +
                                    "(expected an ISW-injected environment variable AppSettings__Clients__*__ClientSecret).",
                                    entry.ClientId);
                    continue;
                }

                loaded[entry.ClientId] = new ClientCredential(
                    _authDefaults.TokenEndpoint, entry.ClientId, entry.ClientSecret, grant, entry.HmacSecret);
            }

            _clients = loaded;

            if (loaded.Count == 0)
                _log.LogWarning("🔐 No ACTIVE clients with a usable secret were loaded from AppSettings:Clients. " +
                                "No outbound clients will be processed until one is configured and the worker restarts.");
            else
                _log.LogInformation("🔐 Client credential load complete: {Loaded} client(s) loaded, {Skipped} skipped. Loaded: {Clients}",
                    loaded.Count, skipped, string.Join(", ", loaded.Keys));

            return Task.CompletedTask;
        }

        public Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required.", nameof(clientId));

            if (_clients.TryGetValue(clientId, out var cred))
                return Task.FromResult(cred);

            throw new InvalidOperationException(
                $"No active credential is configured for client '{clientId}'. " +
                "It may be unregistered, inactive, missing its injected secret, or was added after startup " +
                "(a restart is required to pick it up).");
        }

        public bool IsClientKnown(string clientId) =>
            !string.IsNullOrWhiteSpace(clientId) && _clients.ContainsKey(clientId);
    }
}
