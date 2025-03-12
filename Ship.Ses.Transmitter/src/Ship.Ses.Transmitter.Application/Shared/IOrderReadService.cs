using Ship.Ses.Transmitter.Application.Order.BrowseOrders;
using Ship.Ses.Transmitter.Application.Order.GetOrder;

namespace Ship.Ses.Transmitter.Application.Shared
{
    public interface IOrderReadService
    {
        IQueryable<T> ExecuteSqlQueryAsync<T>(string sql, object[] parameters, CancellationToken cancellationToken) where T : class;

        Task<BrowseOrdersDto> BrowseOrders(Guid customerId, PaginationParameters paginationParameters, CancellationToken cancellationToken);
        Task<OrderDto> GetOrderById(Guid orderId, CancellationToken cancellationToken);
    }
}
