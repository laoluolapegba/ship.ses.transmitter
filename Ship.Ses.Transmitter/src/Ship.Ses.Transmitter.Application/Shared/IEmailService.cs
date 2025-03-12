namespace Ship.Ses.Transmitter.Application.Shared
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
        Task SendWelcomeEmail(string to, Dictionary<string, string> replacements);
    }
}
