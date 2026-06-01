using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Configuration
{
    public sealed class ShipAdminAuthOptions
    {
        public string TokenUrl { get; set; } = default!;
        // Tenant/deployment identity for Admin API auth (heartbeat, metrics). Tenant-level, not a per-record
        // outbound FHIR client. Sent to the IdP as the "clientId" token parameter.
        public string TenantId { get; set; } = default!;
        public string ClientSecret { get; set; } = default!;
        public string GrantType { get; set; } = "client_credentials";
        public string Scope { get; set; } = "billing-full-access";
    }
}
