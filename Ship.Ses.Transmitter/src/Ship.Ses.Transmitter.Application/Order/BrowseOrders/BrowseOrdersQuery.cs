using Ship.Ses.Transmitter.Application.Shared;

namespace Ship.Ses.Transmitter.Application.Order.BrowseOrders
{
    public sealed record BrowseOrdersQuery(Guid CustomerId, PaginationParameters PaginationParameters);
}
