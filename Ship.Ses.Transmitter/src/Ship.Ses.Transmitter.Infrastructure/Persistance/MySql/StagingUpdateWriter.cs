using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.MySql
{
    using Microsoft.EntityFrameworkCore;
    using Ship.Ses.Transmitter.Application.Interfaces;

    public sealed class StagingUpdateWriter : IStagingUpdateWriter
    {
        private readonly IDbContextFactory<ExtractorStagingDbContext> _factory;

        public StagingUpdateWriter(IDbContextFactory<ExtractorStagingDbContext> factory) => _factory = factory;

        public async Task BulkMarkFailedAsync(IEnumerable<long> stagingIds, CancellationToken ct)
        {
            var ids = stagingIds?.Distinct().ToArray() ?? Array.Empty<long>();
            if (ids.Length == 0) return;

            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var now = DateTime.UtcNow;

            foreach (var id in ids)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE ship_fhir_resources
               SET status = 'FAILED', updated_at = {now}
               WHERE id = {id};",
                    ct);
            }

            await tx.CommitAsync(ct);
        }

        public async Task BulkMarkSubmittedAsync(IEnumerable<StagingTransmissionMark> marks, CancellationToken ct)
        {
            var list = marks?.ToList() ?? [];
            if (list.Count == 0) return;

            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var m in list)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE ship_fhir_resources 
               SET status='SUBMITTED',
                   ship_processed_at = {m.SubmittedAtUtc},
                   ship_submit_txid  = {m.ShipSubmitTxId},
                   updated_at        = {m.SubmittedAtUtc}
               WHERE id = {m.StagingId}
                 AND (status IS NULL OR status <> 'SUBMITTED');",
                    ct);
            }

            await tx.CommitAsync(ct);
        }
    }

}
