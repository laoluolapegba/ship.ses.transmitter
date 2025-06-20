using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Sync
{
    public class EfClientSyncConfigProvider : IClientSyncConfigProvider
    {
        private readonly ShipServerDbContext _dbContext;
        private readonly ILogger<EfClientSyncConfigProvider> _logger;

        public EfClientSyncConfigProvider(ShipServerDbContext dbContext, ILogger<EfClientSyncConfigProvider> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<bool> IsClientActiveAsync(string clientId)
        {
            var isActive = await _dbContext.SyncClients
                .AsNoTracking()
                .Where(c => c.ClientId == clientId)
                .Select(c => c.IsActive)
                .FirstOrDefaultAsync();

            _logger.LogDebug("🔎 Client {ClientId} active status: {Status}", clientId, isActive);
            return isActive;
        }

        public async Task<IEnumerable<string>> GetEnabledResourcesAsync(string clientId)
        {
            var resources = await _dbContext.SyncClients
                .AsNoTracking()
                .Where(c => c.ClientId == clientId && c.IsActive)
                .Select(c => c.EnabledResources)
                .FirstOrDefaultAsync();

            _logger.LogDebug("🔎 Client {ClientId} enabled resources: {Resources}", clientId, resources ?? "null");

            return string.IsNullOrEmpty(resources)
                ? Enumerable.Empty<string>()
                : resources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public async Task<bool> IsClientValidAsync(string clientId)
        {
            return await _dbContext.SyncClients
                .AsNoTracking()
                .AnyAsync(c => c.ClientId == clientId && c.IsActive);
        }
        public async Task<string?> GetFacilityIdAsync(string clientId)
        {
            var client = await _dbContext.SyncClients
                .AsNoTracking()
                .Where(c => c.ClientId == clientId && c.IsActive)
                .Select(c => c.FacilityId)
                .FirstOrDefaultAsync();

            return client;
        }
    }
}
