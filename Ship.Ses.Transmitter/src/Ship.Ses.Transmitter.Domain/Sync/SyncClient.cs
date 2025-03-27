namespace Ship.Ses.Transmitter.Domain
{
    public class SyncClient
    {
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string AllowedIps { get; set; } // comma-separated string
        public string ApiKeyHash { get; set; }
        public bool IsActive { get; set; }
        public DateTime RegisteredAt { get; set; }
        public string Description { get; set; }
    }
}