﻿namespace Ship.Ses.Transmitter.Domain.Customers
{
    public interface ICustomerRepository
    {
        Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<Customer?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);
    }
}
