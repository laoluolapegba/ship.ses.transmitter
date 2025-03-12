using Ship.Ses.Transmitter.Application.Customer.Shared;

namespace Ship.Ses.Transmitter.Application.Shared
{
    public interface ICustomerReadService
    {
        IQueryable<T> ExecuteSqlQueryAsync<T>(string sql, object[] parameters, CancellationToken cancellationToken) where T : class;

        Task<CustomerDto> GetCustomerById(Guid customerId, CancellationToken cancellationToken);
    }
}
