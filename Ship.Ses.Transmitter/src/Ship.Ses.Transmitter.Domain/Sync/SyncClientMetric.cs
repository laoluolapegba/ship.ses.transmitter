using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    [Table("ses_sync_client_metrics")]
    public class SyncClientMetric
    {
        [Key]
        [Column("id")]
        public string Id { get; set; }

        [Column("client_id")]
        public string ClientId { get; set; }

        [Column("resource_type")]
        public string ResourceType { get; set; }

        [Column("sync_window_start")]
        public DateTime SyncWindowStart { get; set; }

        [Column("sync_window_end")]
        public DateTime SyncWindowEnd { get; set; }

        [Column("synced_count")]
        public int SyncedCount { get; set; }

        [Column("failed_count")]
        public int FailedCount { get; set; }

        [Column("batch_id")]
        public string BatchId { get; set; }

        [Column("notes")]
        public string Notes { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("created_by")]
        public string CreatedBy { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [Column("updated_by")]
        public string UpdatedBy { get; set; }
    }

}