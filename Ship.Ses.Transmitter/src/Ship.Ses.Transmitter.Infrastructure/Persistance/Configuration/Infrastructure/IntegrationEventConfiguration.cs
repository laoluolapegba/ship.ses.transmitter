using Ship.Ses.Transmitter.Application.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Infrastructure
{
    internal class IntegrationEventConfiguration : IEntityTypeConfiguration<IntegrationEvent>
    {
        public void Configure(EntityTypeBuilder<IntegrationEvent> builder)
        {
            builder.HasKey(x => x.IntergrationEventId);
            builder.Property(x => x.OccuredAt).HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
            builder.Property(x => x.Type).HasMaxLength(500);
            builder.Property(x => x.AssemblyName).HasMaxLength(500);
            builder.Property(x => x.Payload);
            builder.Property(x => x.PublishedAt).HasColumnType("timestamp with time zone");
        }
    }
}
