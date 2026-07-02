using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Acquires an outbound FHIR access token for a specific client, with caching keyed by
    /// (clientId, scope). The credential is resolved per clientId; the scope is route-derived
    /// and passed by the caller (it does not select the credential).
    /// </summary>
    public interface IFhirTokenService
    {
        /// <param name="clientId">The owning client (from the record being transmitted).</param>
        /// <param name="scope">Route-derived scope, or null to request with no/empty scope.</param>
        Task<string> GetAccessTokenAsync(string clientId, string? scope = null, CancellationToken ct = default);
    }
}
