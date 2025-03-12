using Ship.Ses.Transmitter.Domain;

namespace Ship.Ses.Transmitter.Infrastructure.Shared
{
    public class DateTimeProvider : IDateTimeProvider
    {
        private DateTime _date;
        public DateTimeProvider()
        {
            _date = DateTime.UtcNow;
        }

        public DateTime UtcNow => _date;

        public void Set(DateTime dateTime)
        {
            _date = dateTime;
        }
    }
}
