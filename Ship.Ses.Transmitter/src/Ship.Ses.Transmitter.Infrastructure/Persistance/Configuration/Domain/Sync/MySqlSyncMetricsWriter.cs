using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ship.Ses.Transmitter.Application.Sync;
//using Dapper;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using System;
using System.Data;
using System.Net;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Sync
{
    public class MySqlSyncMetricsWriter : ISyncMetricsWriter
    {
        private readonly ShipServerDbContext _dbContext;
        private readonly ILogger<MySqlSyncMetricsWriter> _logger;

        public MySqlSyncMetricsWriter(ShipServerDbContext dbContext,
            ILogger<MySqlSyncMetricsWriter> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task WriteStatusAsync(SyncClientStatus status)
        {
            if (status is null) throw new ArgumentNullException(nameof(status));
            if (string.IsNullOrWhiteSpace(status.ClientId))
                throw new ArgumentException("ClientId is required", nameof(status));

            _logger.LogDebug("Upserting status for client: {ClientId}", status.ClientId);

            var existing = await _dbContext.SyncClientStatuses
                .AsTracking()
                .FirstOrDefaultAsync(x => x.ClientId == status.ClientId);

            if (existing != null)
            {
                _logger.LogDebug("Updating existing status record (accumulating totals).");

                // Accumulate totals (do NOT reset to zero on restart)
                // Treat negative inputs as 0 to avoid accidental decrements.
                existing.TotalSynced += Math.Max(0, status.TotalSynced);
                existing.TotalFailed += Math.Max(0, status.TotalFailed);

                // Only overwrite optional fields when a non-null value is provided
                existing.Status = status.Status ?? existing.Status;
                existing.CurrentBatchId = status.CurrentBatchId ?? existing.CurrentBatchId;
                existing.LastError = status.LastError ?? existing.LastError;
                existing.IpAddress = status.IpAddress ?? existing.IpAddress;
                existing.Hostname = status.Hostname ?? existing.Hostname;
                existing.Version = status.Version ?? existing.Version;
                existing.SignatureHash = status.SignatureHash ?? existing.SignatureHash;

                // Timestamps
                existing.LastCheckIn = status.LastCheckIn ?? DateTime.UtcNow;
                if (status.LastSyncedAt.HasValue)
                    existing.LastSyncedAt = status.LastSyncedAt;

            }
            else
            {
                _logger.LogDebug("➕ Inserting new status record.");

                // For a brand-new row, take fields as-is; ensure LastCheckIn has a sensible default
                status.LastCheckIn ??= DateTime.UtcNow;
                await _dbContext.SyncClientStatuses.AddAsync(status);
            }

            var result = await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Status upserted for {ClientId} (affected rows: {Result})", status.ClientId, result);
        }

        //public async Task WriteStatusAsync(SyncClientStatus status)
        //{
        //    _logger.LogDebug("Upserting status for client: {ClientId}", status.ClientId);

        //    var existing = await _dbContext.SyncClientStatuses
        //        .AsTracking()
        //        .FirstOrDefaultAsync(x => x.ClientId == status.ClientId);

        //    if (existing != null)
        //    {
        //        _logger.LogDebug("Updating existing status record.");
        //        _dbContext.Entry(existing).CurrentValues.SetValues(status);
        //    }
        //    else
        //    {
        //        _logger.LogDebug("➕ Inserting new status record.");
        //        await _dbContext.SyncClientStatuses.AddAsync(status);
        //    }

        //    var result = await _dbContext.SaveChangesAsync();
        //    _logger.LogInformation("Status upserted for {ClientId} (affected rows: {Result})", status.ClientId, result);
        //}

        public async Task WriteMetricAsync(SyncClientMetric metric)
        {
            _logger.LogDebug("Inserting metric for {ClientId} - Resource: {ResourceType}, Synced: {Synced}, Failed: {Failed}",
                metric.ClientId, metric.ResourceType, metric.SyncedCount, metric.FailedCount);

            await _dbContext.SyncClientMetrics.AddAsync(metric);
            var result = await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Metric written for {ClientId}/{ResourceType} (ID: {Id})", metric.ClientId, metric.ResourceType, metric.Id);
        }
        public async Task WriteMetricsAsync(IEnumerable<SyncClientMetric> metrics)
        {
            _dbContext.SyncClientMetrics.AddRange(metrics);
            await _dbContext.SaveChangesAsync();
        }
        
    }
}