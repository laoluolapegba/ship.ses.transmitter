using System;

namespace Ship.Ses.Transmitter.Infrastructure.Security
{
    /// <summary>
    /// Vault connection + secret-schema settings read from OS environment variables — the same mechanism
    /// the SeS Ingestor uses, so DevOps configures Vault clients once and both services retrieve the same
    /// way. There is no <c>appsettings</c> section. <c>VAULT_ADDR</c> and <c>VAULT_TOKEN</c> are required
    /// (the worker exits at startup if they are unset); the rest are optional with sensible defaults.
    /// <para>
    /// Per-client secrets are read from <c>{Mount}/data/{PathTemplate}</c> (KV v2) where <c>{clientId}</c>
    /// — the Vault folder name — is substituted. The folder name <b>is</b> the clientId (no override key).
    /// </para>
    /// </summary>
    public sealed record VaultClientSecretSettings
    {
        private const string ClientIdPlaceholder = "{clientId}";

        public string? Address { get; init; }
        public string? Token { get; init; }
        public string Mount { get; init; } = "secret";
        public int KvVersion { get; init; } = 2;
        public string PathTemplate { get; init; } = "ses/clients/{clientId}";
        public string SecretKey { get; init; } = "clientSecret";
        public int RequestTimeoutSeconds { get; init; } = 10;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Token);

        /// <summary>The listing prefix: everything in <see cref="PathTemplate"/> before <c>{clientId}</c>.</summary>
        public string ListPrefix()
        {
            var idx = PathTemplate.IndexOf(ClientIdPlaceholder, StringComparison.Ordinal);
            return (idx >= 0 ? PathTemplate[..idx] : PathTemplate).Trim('/');
        }

        /// <summary>The logical secret path for a clientId (placeholder substituted; not yet KV-prefixed).</summary>
        public string ResolvePath(string clientId) =>
            PathTemplate.Replace(ClientIdPlaceholder, clientId, StringComparison.Ordinal).Trim('/');

        public static VaultClientSecretSettings FromEnvironment() => new()
        {
            Address = Environment.GetEnvironmentVariable("VAULT_ADDR"),
            Token = Environment.GetEnvironmentVariable("VAULT_TOKEN"),
            Mount = Env("VAULT_MOUNT", "secret"),
            KvVersion = int.TryParse(Environment.GetEnvironmentVariable("VAULT_KV_VERSION"), out var v) ? v : 2,
            PathTemplate = Env("VAULT_PATH_TEMPLATE", "ses/clients/{clientId}"),
            SecretKey = Env("VAULT_SECRET_KEY", "clientSecret"),
            RequestTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("VAULT_REQUEST_TIMEOUT_SECONDS"), out var t) && t > 0 ? t : 10
        };

        private static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }
    }
}
