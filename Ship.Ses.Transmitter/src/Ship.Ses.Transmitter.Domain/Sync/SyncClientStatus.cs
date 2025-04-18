using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    [Table("ses_sync_client_status")]
    public class SyncClientStatus
    {
        [Key]
        [Column("client_id")]
        public string ClientId { get; set; }

        [Column("status")]
        public string Status { get; set; }

        [Column("last_check_in")]
        public DateTime? LastCheckIn { get; set; }

        [Column("last_synced_at")]
        public DateTime? LastSyncedAt { get; set; }

        [Column("total_synced")]
        public int TotalSynced { get; set; }

        [Column("total_failed")]
        public int TotalFailed { get; set; }

        [Column("current_batch_id")]
        public string CurrentBatchId { get; set; }

        [Column("last_error")]
        public string? LastError { get; set; }

        [Column("ip_address")]
        public string IpAddress { get; set; }

        [Column("hostname")]
        public string Hostname { get; set; }

        [Column("version")]
        public string Version { get; set; }

        [Column("signature_hash")]
        public string? SignatureHash { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

}