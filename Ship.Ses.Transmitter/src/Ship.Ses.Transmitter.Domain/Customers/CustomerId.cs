using Ship.Ses.Transmitter.Domain.Customers.Exceptions;

namespace Ship.Ses.Transmitter.Domain.Customers
{
    public sealed record CustomerId(Guid Value)
    {
        public static implicit operator Guid(CustomerId id) => id.Value;

        public static implicit operator CustomerId(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new InvalidCustomerIdDomainException(id);
            }
            return new CustomerId(id);
        }
    }
}
