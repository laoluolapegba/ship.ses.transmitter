using System;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    /// <summary>
    /// Selects where outbound client credentials are resolved from, and (for Vault) how.
    /// Bound from the <c>ClientCredentials</c> configuration section.
    /// </summary>
    public sealed class ClientCredentialsOptions
    {
        /// <summary>"Config" (single-client fallback via AuthSettings) or "Vault".</summary>
        public string Source { get; set; } = "Config";

        public VaultOptions Vault { get; set; } = new();
    }

    /// <summary>
    /// Vault settings for per-client secret resolution. Mirrors the Ingestor's operating model so DevOps
    /// configures Vault clients once: every registered client under the <see cref="PathTemplate"/> prefix is
    /// discovered and read <b>once at startup</b> (no per-request calls, no TTL cache). Adding or rotating a
    /// client requires a restart. KV v2 reads <c>{KvMount}/data/{path}</c> and lists <c>{KvMount}/metadata/{prefix}</c>.
    /// </summary>
    public sealed class VaultOptions
    {
        private const string ClientIdPlaceholder = "{clientId}";

        /// <summary>Vault server base address, e.g. https://vault.internal:8200.</summary>
        public string Address { get; set; } = default!;

        /// <summary>Vault token (supply via env/secret store — never commit). Needs <c>list</c> on the prefix and <c>read</c> on the client paths.</summary>
        public string Token { get; set; } = default!;

        /// <summary>KV mount path.</summary>
        public string KvMount { get; set; } = "secret";

        /// <summary>KV engine version (controls the <c>data</c>/<c>metadata</c> path segments).</summary>
        public int KvVersion { get; set; } = 2;

        /// <summary>
        /// Logical per-client secret path; <c>{clientId}</c> is substituted. Do not include <c>data</c>/<c>metadata</c>
        /// — the adapter inserts those for KV v2. The folder name is the clientId. Design: ses/clients/{clientId}/hmac.
        /// </summary>
        public string PathTemplate { get; set; } = "ses/clients/{clientId}/hmac";

        /// <summary>Key inside the secret holding the client secret / HMAC key.</summary>
        public string SecretKey { get; set; } = "clientSecret";

        /// <summary>Optional key holding the outbound client_id; defaults to the folder clientId when absent.</summary>
        public string? ClientIdKey { get; set; } = "clientId";

        /// <summary>Optional field; value <c>revoked</c>/<c>inactive</c> disables the client.</summary>
        public string StatusKey { get; set; } = "status";

        /// <summary>Optional boolean field; <c>false</c> disables the client.</summary>
        public string IsActiveKey { get; set; } = "isActive";

        /// <summary>Optional boolean field; <c>true</c> disables the client.</summary>
        public string IsRevokedKey { get; set; } = "isRevoked";

        public int RequestTimeoutSeconds { get; set; } = 10;

        /// <summary>The listing prefix: everything in <see cref="PathTemplate"/> before <c>{clientId}</c>.</summary>
        public string ListPrefix()
        {
            var template = PathTemplate ?? string.Empty;
            var idx = template.IndexOf(ClientIdPlaceholder, StringComparison.Ordinal);
            var prefix = idx >= 0 ? template[..idx] : template;
            return prefix.Trim('/');
        }

        /// <summary>The logical secret path for a clientId (placeholder substituted; not yet KV-prefixed).</summary>
        public string ResolvePath(string clientId) =>
            (PathTemplate ?? string.Empty).Replace(ClientIdPlaceholder, clientId, StringComparison.Ordinal).Trim('/');
    }
}
