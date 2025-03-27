namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public record ShipServerSqlDb (string ConnectionString);
    public record MsSql(string ConnectionString);
}
