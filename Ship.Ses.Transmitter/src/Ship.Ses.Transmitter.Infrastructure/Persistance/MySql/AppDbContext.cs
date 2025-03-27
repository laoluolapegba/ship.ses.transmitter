using Microsoft.EntityFrameworkCore;
using Ship.Ses.Transmitter.Domain;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.MySql
{
    public class AppDbContext : DbContext, IAppDbContext
    {

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }
        public DbSet<SyncClientStatus> SyncClientStatuses { get; set; }
        //public DbSet<SyncClient> SyncClients { get; set; }
        public DbSet<SyncClientMetric> SyncClientMetrics { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SyncClientStatus>().HasKey(x => x.ClientId);
            modelBuilder.Entity<SyncClientMetric>().HasKey(x => x.Id);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }

    public interface IAppDbContext
    {
        //public DbSet<SyncClient>  SyncClients { get; set; }
        public DbSet<SyncClientMetric> SyncClientMetrics { get; set; }
        public DbSet<SyncClientStatus> SyncClientStatuses { get; set; }
    }
}
