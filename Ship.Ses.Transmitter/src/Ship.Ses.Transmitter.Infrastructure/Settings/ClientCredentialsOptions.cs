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
    /// Vault KV v2 settings for per-client secret resolution.
    /// Secrets are read from <c>{KvMount}/data/{PathTemplate}</c> where <c>{clientId}</c> is substituted.
    /// </summary>
    public sealed class VaultOptions
    {
        /// <summary>Vault server base address, e.g. https://vault.internal:8200.</summary>
        public string Address { get; set; } = default!;

        /// <summary>Vault token (supply via env/secret store — never commit).</summary>
        public string Token { get; set; } = default!;

        /// <summary>KV v2 mount path.</summary>
        public string KvMount { get; set; } = "secret";

        /// <summary>Per-client secret path; <c>{clientId}</c> is substituted. Design: secret/ses/clients/{clientId}/hmac.</summary>
        public string PathTemplate { get; set; } = "ses/clients/{clientId}/hmac";

        /// <summary>Key inside the secret holding the client secret / HMAC key.</summary>
        public string SecretKey { get; set; } = "clientSecret";

        /// <summary>Optional key holding the outbound client_id; defaults to the requested clientId when absent.</summary>
        public string? ClientIdKey { get; set; } = "clientId";

        /// <summary>How long a resolved credential is cached before re-reading Vault (picks up rotation).</summary>
        public int CacheTtlSeconds { get; set; } = 300;

        public int RequestTimeoutSeconds { get; set; } = 10;
    }
}
