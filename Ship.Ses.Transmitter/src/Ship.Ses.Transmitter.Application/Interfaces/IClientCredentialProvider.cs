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
    /// The implementation loads the registered clients once at startup (<see cref="InitializeAsync"/>) from
    /// configuration (the <c>AppSettings:Clients</c> list), keeping only <c>ACTIVE</c> clients that have a
    /// usable secret. Secret values are injected into the runtime as environment variables before startup by
    /// the organization's ISW secret-injection mechanism (a Vault agent) and bound over the committed
    /// placeholders — the application makes no Vault API calls. There are no per-request lookups and no TTL:
    /// only clients present at startup are processed, and adding/rotating a client requires a restart.
    /// </para>
    /// </summary>
    public interface IClientCredentialProvider
    {
        /// <summary>
        /// Loads the set of valid clients once at startup: read all <c>ACTIVE</c> clients with a usable secret
        /// from configuration into memory. Safe to call exactly once before workers begin processing.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Resolves the outbound credential for a client. Throws if the client is not known/valid.</summary>
        Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default);

        /// <summary>
        /// True if the client is one we may process — present in the startup-loaded <c>ACTIVE</c> set.
        /// Used to skip records for unknown clients.
        /// </summary>
        bool IsClientKnown(string clientId);
    }

    /// <summary>
    /// Non-scope credential material needed to request an outbound access token for a client.
    /// Scope is intentionally excluded — it is route-derived and passed separately.
    /// <para>
    /// <see cref="HmacSecret"/> is the client's shared HMAC key (for request signing). It is carried
    /// alongside the OAuth secret so both resolve per <c>clientId</c> from the same source; it is optional
    /// and not used for token acquisition.
    /// </para>
    /// </summary>
    public sealed record ClientCredential(
        string TokenEndpoint,
        string ClientId,
        string ClientSecret,
        string GrantType = "client_credentials",
        string? HmacSecret = null);
}
