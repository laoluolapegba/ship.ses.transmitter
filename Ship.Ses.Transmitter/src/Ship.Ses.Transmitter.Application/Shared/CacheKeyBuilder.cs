namespace Ship.Ses.Transmitter.Application.Shared
{
    public static class CacheKeyBuilder
    {
        public static string GetCustomerKey(Guid id) => $"customer:{id}";
        public static string GetOrderKey(Guid id) => $"order:{id}";
    }
}
