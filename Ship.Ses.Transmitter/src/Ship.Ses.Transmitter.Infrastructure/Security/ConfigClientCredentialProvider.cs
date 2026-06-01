using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Interim <see cref="IClientCredentialProvider"/> that returns the single configured
    /// <see cref="AuthSettings"/> credential for every clientId (single-client fallback).
    /// <para>
    /// This preserves today's behaviour while introducing the per-client seam. Phase 4 of the
    /// multi-client build plan replaces this with a Vault-backed lookup keyed by clientId
    /// (<c>secret/ses/clients/{clientId}/hmac</c>).
    /// </para>
    /// </summary>
    public sealed class ConfigClientCredentialProvider : IClientCredentialProvider
    {
        private readonly AuthSettings _auth;
        private readonly ILogger<ConfigClientCredentialProvider> _log;

        public ConfigClientCredentialProvider(IOptions<AuthSettings> auth, ILogger<ConfigClientCredentialProvider> log)
        {
            _auth = auth?.Value ?? throw new ArgumentNullException(nameof(auth));
            _log = log;
        }

        // Single-client fallback has no Vault discovery step.
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        // Open mode: any non-blank clientId is processed against the one configured credential.
        public bool IsClientKnown(string clientId) => !string.IsNullOrWhiteSpace(clientId);

        public Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required.", nameof(clientId));

            // NOTE: single-client fallback — clientId is not yet used to vary the credential.
            _log.LogDebug("Resolving outbound credential for client {ClientId} from AuthSettings (single-client fallback).", clientId);

            var grant = string.IsNullOrWhiteSpace(_auth.GrantType) ? "client_credentials" : _auth.GrantType;
            return Task.FromResult(new ClientCredential(_auth.TokenEndpoint, _auth.ClientId, _auth.ClientSecret, grant));
        }
    }
}
