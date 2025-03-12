namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public record Smtp(string Server, int Port, string User, string Password, bool EnableSsl, string EmailFrom);

}
