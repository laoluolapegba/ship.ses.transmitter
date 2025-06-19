namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public record AppSettings
    {
        public required ShipServerSqlDb ShipServerSqlDb { get; init; }
        public required MsSql MsSql { get; init; }
        public required Cache Cache { get; init; }
        public required Smtp Smtp { get; init; }
        public required Authentication Authentication { get; init; }
        public required Cors Cors { get; init; }
    }
}
