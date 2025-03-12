namespace Ship.Ses.Transmitter.Infrastructure.Exceptions
{
    public class CommandValidationException : Exception
    {
        public Dictionary<string, string[]> Content { get; }
        public CommandValidationException(string msg, Dictionary<string, string[]> content) : base(msg)
        {
            Content = content;
        }
    }
}
