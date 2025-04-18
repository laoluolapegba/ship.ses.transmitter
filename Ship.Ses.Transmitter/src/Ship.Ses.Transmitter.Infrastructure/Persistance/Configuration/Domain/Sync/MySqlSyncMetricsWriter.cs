using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ship.Ses.Transmitter.Application.Sync;



//using Dapper;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;

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
            _logger.LogDebug("🔄 Upserting status for client: {ClientId}", status.ClientId);

            var existing = await _dbContext.SyncClientStatuses
                .AsTracking()
                .FirstOrDefaultAsync(x => x.ClientId == status.ClientId);

            if (existing != null)
            {
                _logger.LogDebug("📝 Updating existing status record.");
                _dbContext.Entry(existing).CurrentValues.SetValues(status);
            }
            else
            {
                _logger.LogDebug("➕ Inserting new status record.");
                await _dbContext.SyncClientStatuses.AddAsync(status);
            }

            var result = await _dbContext.SaveChangesAsync();
            _logger.LogInformation("✅ Status upserted for {ClientId} (affected rows: {Result})", status.ClientId, result);
        }

        public async Task WriteMetricAsync(SyncClientMetric metric)
        {
            _logger.LogDebug("📊 Inserting metric for {ClientId} - Resource: {ResourceType}, Synced: {Synced}, Failed: {Failed}",
                metric.ClientId, metric.ResourceType, metric.SyncedCount, metric.FailedCount);

            await _dbContext.SyncClientMetrics.AddAsync(metric);
            var result = await _dbContext.SaveChangesAsync();

            _logger.LogInformation("✅ Metric written for {ClientId}/{ResourceType} (ID: {Id})", metric.ClientId, metric.ResourceType, metric.Id);
        }
        public async Task WriteMetricsAsync(IEnumerable<SyncClientMetric> metrics)
        {
            _dbContext.SyncClientMetrics.AddRange(metrics);
            await _dbContext.SaveChangesAsync();
        }
    }
}