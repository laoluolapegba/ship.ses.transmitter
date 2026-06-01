using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Reads from a backing secret store (Vault KV v1/v2). Supports reading a single secret's data map
    /// and listing the client ids registered under a prefix (for startup discovery).
    /// </summary>
    public interface IVaultSecretReader
    {
        /// <summary>Reads the secret at <paramref name="path"/>; returns its data map, or <c>null</c> if not found.</summary>
        Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken ct = default);

        /// <summary>Lists the immediate child keys (client ids) under <paramref name="listPrefix"/>. Empty if none/denied.</summary>
        Task<IReadOnlyList<string>> ListClientIdsAsync(string listPrefix, CancellationToken ct = default);
    }
}
