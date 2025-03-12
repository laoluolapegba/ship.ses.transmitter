namespace Ship.Ses.Transmitter.Domain.Orders.DomainEvents
{
    public sealed record OrderCreatedDomainEvent(Guid OrderId, Guid CustomerId) : IDomainEvent;
}
