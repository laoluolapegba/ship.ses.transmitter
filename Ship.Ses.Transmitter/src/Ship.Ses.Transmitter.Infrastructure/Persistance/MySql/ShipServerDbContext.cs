using Microsoft.EntityFrameworkCore;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.MySql
{
    public class ShipServerDbContext : DbContext, IShipServerDbContext
    {

        public ShipServerDbContext(DbContextOptions<ShipServerDbContext> options)
            : base(options)
        {
        }
        public DbSet<SyncClientStatus> SyncClientStatuses { get; set; }
        public DbSet<SyncClient> SyncClients { get; set; }
        public DbSet<SyncClientMetric> SyncClientMetrics { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncClientStatus>().HasKey(x => x.ClientId);
            modelBuilder.Entity<SyncClientMetric>().HasKey(x => x.Id);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShipServerDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }

    public interface IShipServerDbContext
    {
        public DbSet<SyncClient>  SyncClients { get; set; }
        public DbSet<SyncClientMetric> SyncClientMetrics { get; set; }
        public DbSet<SyncClientStatus> SyncClientStatuses { get; set; }
    }
}
