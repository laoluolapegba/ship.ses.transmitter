using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public class SeSClientOptions
    {
        /// <summary>
        /// Tenant/deployment identity for tenant-level concerns (Admin API, heartbeat, metrics, sync
        /// enable/disable). This is NOT used to select outbound FHIR credentials — those are resolved
        /// per-record by <c>clientId</c>. Prefer <see cref="TenantId"/>; <see cref="ClientId"/> is a
        /// backward-compatible alias.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>Deprecated alias for <see cref="TenantId"/> (older config used "ClientId").</summary>
        public string? ClientId { get; set; }

        /// <summary>The effective tenant identity (TenantId, falling back to the legacy ClientId).</summary>
        public string? EffectiveTenantId => string.IsNullOrWhiteSpace(TenantId) ? ClientId : TenantId;

        public string SyncEndpoint { get; set; }
        public int HeartbeatSeconds { get; set; } = 90;
        public int MetricsFlushSeconds { get; set; } = 300;
        public bool UseShipAdminApi { get; set; } = true;
    }
}
