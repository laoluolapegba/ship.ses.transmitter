using Ship.Ses.Transmitter.Application.Shared;
using Ship.Ses.Transmitter.Domain;

namespace Ship.Ses.Transmitter.Infrastructure.Events
{
    public interface IEventMapper
    {
        IntegrationEvent Map(IDomainEvent domainEvent);
    }
}
