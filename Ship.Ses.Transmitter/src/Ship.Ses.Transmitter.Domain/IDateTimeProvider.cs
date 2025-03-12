namespace Ship.Ses.Transmitter.Domain
{
    public interface IDateTimeProvider
    {
        public DateTime UtcNow { get; }
        public void Set(DateTime dateTime);
    }
}
