﻿namespace Ship.Ses.Transmitter.Domain.Customers.Exceptions
{
    public class InvalidCustomerAgeDomainException : DomainException
    {
        public InvalidCustomerAgeDomainException()
            : base("Customer has to be at least 18 years old.")
        {
        }
    }
}
