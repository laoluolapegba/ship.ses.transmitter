using Ship.Ses.Transmitter.Domain.Customers.Exceptions;

namespace Ship.Ses.Transmitter.Domain.Orders
{
    public sealed record ShippingAddress
    {
        public string Street { get; private set; }
        public string PostalCode { get; private set; }

        public ShippingAddress(string street, string postalCode)
        {
            if (string.IsNullOrWhiteSpace(street) || street.Length > OrderConstants.Order.StreetMaxLength)
                throw new InvalidAddressDomainException(nameof(street));
            if (string.IsNullOrWhiteSpace(postalCode) || postalCode.Length > OrderConstants.Order.PostalCodeMaxLength)
                throw new InvalidAddressDomainException(nameof(postalCode));

            Street = street;
            PostalCode = postalCode;
        }
    }
}
