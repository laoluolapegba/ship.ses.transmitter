using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    [Table("ses_sync_clients")]
    public class SyncClient
    {
        [Key]
        [Column("facility_id")]
        public required string FacilityId { get; set; }

        [Column("client_id")]
        public required string ClientId { get; set; }

        [Column("client_name")]
        public required string ClientName { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("enabled_resources")]
        public string EnabledResources { get; set; } // e.g. "Patient,Encounter"

        [Column("allowed_ips")]
        public string AllowedIpRanges { get; set; } 

        [Column("sync_endpoint")]
        public string SyncEndpoint { get; set; } // Optional: callback or handshake endpoint

        [Column("security_key")]
        public string SecurityKey { get; set; } // Optional: used for HMAC/JWT signatures

        [Column("certificate_thumbprint")]
        public string CertificateThumbprint { get; set; } // Optional: pinned client cert

        [Column("last_seen")]
        public DateTime? LastSeen { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
        
        
    }
}