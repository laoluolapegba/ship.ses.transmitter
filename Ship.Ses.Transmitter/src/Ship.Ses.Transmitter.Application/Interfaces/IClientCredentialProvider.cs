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
    /// Today the only implementation reads the single configured <c>AuthSettings</c> (a fallback for
    /// every clientId). A future Vault-backed implementation resolves per-client secrets from
    /// <c>secret/ses/clients/{clientId}/hmac</c>.
    /// </para>
    /// </summary>
    public interface IClientCredentialProvider
    {
        Task<ClientCredential> GetAsync(string clientId, CancellationToken ct = default);
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
