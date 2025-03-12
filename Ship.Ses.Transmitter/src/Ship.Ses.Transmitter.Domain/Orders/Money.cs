namespace Ship.Ses.Transmitter.Domain.Orders
{
    public sealed record Money(decimal Amount, string Currency = "USD");
}
