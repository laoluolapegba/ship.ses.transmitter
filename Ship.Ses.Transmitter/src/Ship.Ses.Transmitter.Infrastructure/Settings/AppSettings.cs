namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public record AppSettings
    {
        public required DatabaseSettings ShipServerSqlDb { get; init; } // for ShipServerDbContext
        public required DatabaseSettings EmrDb { get; init; }           // for Extractor/Staging updater
        public required Cache Cache { get; init; }
        public required Smtp Smtp { get; init; }
        public required Authentication Authentication { get; init; }
        public required Cors Cors { get; init; }
    }
    //public record ShipServerSqlDb(string ConnectionString);

    public record DatabaseSettings
    {
        public required string ConnectionString { get; init; }
        /// <summary>mysql | postgres | sqlserver</summary>
        public required string DbType { get; init; }
    }
}
