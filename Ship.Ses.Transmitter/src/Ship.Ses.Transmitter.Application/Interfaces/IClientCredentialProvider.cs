using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    /// <summary>
    /// Resolves the OUTBOUND credential used to authenticate to SHIP services for a given client.
    /// <para>
    /// Design contract: the same credential is used across all SHIP target systems (PDS, SCR, …);
    /// resolution is keyed by <c>clientId</c> only — never by target system. The target system /
    /// shipService drives routing and scope, not credential selection.
    /// </para>
    /// <para>
    /// The Vault-backed implementation discovers every registered client under the configured prefix and
    /// loads each client's secret once at startup (<see cref="InitializeAsync"/>) — mirroring the Ingestor's
    /// model. There are no per-request Vault calls and no TTL: only the clients present (and active) at
    /// startup are processed, and adding/rotating a client requires a restart. The <c>Config</c>
    /// implementation is a single-client fallback that treats every clientId as known.
    /// </para>
    /// </summary>
    public interface IClientCredentialProvider
    {
        /// <summary>
        /// Loads the set of valid clients once at startup. Vault: discover + read all active clients into
        /// memory. Config: a no-op. Safe to call exactly once before workers begin processing.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Resolves the outbound credential for a client. Throws if the client is not known/valid.</summary>
        Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default);

        /// <summary>
        /// True if the client is one we may process. Vault: present in the startup-loaded active set.
        /// Config: any non-blank clientId (single-client fallback). Used to skip records for unknown clients.
        /// </summary>
        bool IsClientKnown(string clientId);
    }

    /// <summary>
    /// Non-scope credential material needed to request an outbound access token for a client.
    /// Scope is intentionally excluded — it is route-derived and passed separately.
    /// </summary>
    public sealed record ClientCredential(
        string TokenEndpoint,
        string ClientId,
        string ClientSecret,
        string GrantType = "client_credentials");
}
