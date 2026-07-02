using System;
using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    /// <summary>
    /// The per-client credential directory and HMAC settings, bound from the <c>AppSettings</c> section
    /// (keys <c>AppSettings:Clients</c> and <c>AppSettings:Hmac</c>). This is the transmitter's only source
    /// of outbound client secrets — there are no Vault API calls, no <c>VAULT_ADDR</c>/<c>VAULT_TOKEN</c>,
    /// and no <c>X-Vault-Token</c> handling.
    /// <para>
    /// Secret values are <b>injected into the runtime as environment variables before startup</b> by the
    /// organization's ISW secret-injection mechanism (a Vault agent/sidecar), and .NET's environment-variable
    /// configuration provider binds them over the placeholders committed in <c>appsettings.json</c>. For
    /// example the second client's OAuth secret is supplied as
    /// <c>AppSettings__Clients__1__ClientSecret</c> and its HMAC key as <c>AppSettings__Clients__1__HmacSecret</c>.
    /// The application therefore never talks to Vault: it only reads its own configuration.
    /// </para>
    /// </summary>
    public sealed class ClientDirectoryOptions
    {
        /// <summary>The registered clients. Only entries with <c>Status = ACTIVE</c> are loaded for processing.</summary>
        public List<ClientCredentialEntry> Clients { get; set; } = new();

        /// <summary>HMAC settings shared across clients (per-client HMAC key lives on each <see cref="ClientCredentialEntry"/>).</summary>
        public HmacOptions Hmac { get; set; } = new();
    }

    /// <summary>
    /// One registered client's credential material. <see cref="ClientSecret"/> and <see cref="HmacSecret"/>
    /// carry placeholder text in committed config and are overridden at runtime by ISW-injected environment
    /// variables. The Vault store behind ISW is the source of truth; the application never reads it directly.
    /// </summary>
    public sealed class ClientCredentialEntry
    {
        public string ClientId { get; set; } = default!;

        /// <summary>OAuth client secret used to obtain the outbound SHIP access token.</summary>
        public string? ClientSecret { get; set; }

        /// <summary>Shared HMAC key for this client (request signing/verification).</summary>
        public string? HmacSecret { get; set; }

        /// <summary><c>ACTIVE</c> | anything else. Only <c>ACTIVE</c> clients are loaded.</summary>
        public string Status { get; set; } = "ACTIVE";

        public bool IsActive =>
            string.Equals(Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HMAC request-signing settings, bound from <c>AppSettings:Hmac</c>. Mirrors the Ingestor's shape so the
    /// two services share one configuration model. Per-client keys are not here — they live on each
    /// <see cref="ClientCredentialEntry.HmacSecret"/>.
    /// </summary>
    public sealed class HmacOptions
    {
        public bool Enabled { get; set; }
        public bool RequireJwtAlso { get; set; }
        public string SignatureHeader { get; set; } = "X-SHIP-Signature";
        public string TimestampHeader { get; set; } = "X-SHIP-Date";
        public string NonceHeader { get; set; } = "X-SHIP-Nonce";
        public int AllowedClockSkewSeconds { get; set; } = 300;
        public string HmacAlgo { get; set; } = "HMACSHA256";
        public string[] BypassPaths { get; set; } = Array.Empty<string>();
    }
}
