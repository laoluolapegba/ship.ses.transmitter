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

        public async Task BulkMarkSubmittedAsync(IEnumerable<StagingTransmissionMark> marks, CancellationToken ct)
        {
            var list = marks?.ToList() ?? [];
            if (list.Count == 0) return;

            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Fast updates; one round-trip per row is fine for moderate batches.
            // If you need extreme throughput, switch to ADO.NET and batch statements within the transaction.
            foreach (var m in list)
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE fhir_staging 
                    SET status = 'SUBMITTED',
                        ship_submit_txid  = {1},
                        updated_at = {0}
                  WHERE id = {2} AND (status IS NULL OR status <> 'SUBMITTED')",
                    m.SubmittedAtUtc, m.ShipSubmitTxId, m.StagingId, ct);
            }

            await tx.CommitAsync(ct);
        }

        public async Task BulkMarkFailedAsync(IEnumerable<long> stagingIds, CancellationToken ct)
        {
            var ids = stagingIds?.Distinct().ToList() ?? [];
            if (ids.Count == 0) return;

            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var id in ids)
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE fhir_staging 
                    SET status = 'FAILED',
                        updated_at = UTC_TIMESTAMP(6)
                  WHERE id = {0}", id, ct);
            }

            await tx.CommitAsync(ct);
        }
    }

}
