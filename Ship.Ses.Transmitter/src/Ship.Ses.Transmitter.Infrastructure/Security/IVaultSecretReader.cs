using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Reads a key/value secret from a backing store (Vault KV v2). Returns the secret's data map,
    /// or <c>null</c> if the path does not exist.
    /// </summary>
    public interface IVaultSecretReader
    {
        Task<IReadOnlyDictionary<string, string>?> ReadAsync(string path, CancellationToken ct = default);
    }
}
