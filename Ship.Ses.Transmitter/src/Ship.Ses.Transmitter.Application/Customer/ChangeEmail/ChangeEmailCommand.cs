namespace Ship.Ses.Transmitter.Application.Customer.ChangeEmail
{
    public sealed record ChangeEmailCommand(Guid CustomerId, string NewEmail);
}
