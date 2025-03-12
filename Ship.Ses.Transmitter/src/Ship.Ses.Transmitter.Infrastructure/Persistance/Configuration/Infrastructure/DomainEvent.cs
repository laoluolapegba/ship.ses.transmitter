namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Infrastructure
{
    public sealed record DomainEvent(Guid DomainEventId, DateTime OccuredAt, string Type, string AssemblyName, string Payload, DateTime? ComplatedAt = null);
}
