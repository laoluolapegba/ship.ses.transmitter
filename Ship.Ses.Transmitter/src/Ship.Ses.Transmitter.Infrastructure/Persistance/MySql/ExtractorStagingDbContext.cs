using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.MySql
{
    using Microsoft.EntityFrameworkCore;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public sealed class ExtractorStagingDbContext : DbContext
    {
        public ExtractorStagingDbContext(DbContextOptions<ExtractorStagingDbContext> options) : base(options) { }
        public DbSet<FhirStagingRow> FhirStaging => Set<FhirStagingRow>();
    }
    [Table("ship_fhir_resources")]
    public sealed class FhirStagingRow
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        //[Column("ship_processed_at")]
        //public DateTime? ShipProcessedAt { get; set; }

        //[Column("ship_submitted_at")]
        //public DateTime? ShipSubmittedAt { get; set; } 

        [Column("ship_submit_txid")]
        public string? ShipSubmitTxId { get; set; } 

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
