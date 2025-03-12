using Ship.Ses.Transmitter.Domain;

namespace Ship.Ses.Transmitter.Infrastructure.Events
{
    public interface IDomainEventDispatcher
    {
        Task Dispatch(IDomainEvent domainEvent);
    }
}
