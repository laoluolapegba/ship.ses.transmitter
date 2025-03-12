namespace Ship.Ses.Transmitter.Domain.Customers.DomainEvents
{
    public sealed record CustomerEmailVerifiedDomainEvent(string NewEmailAddress) : IDomainEvent;
}
