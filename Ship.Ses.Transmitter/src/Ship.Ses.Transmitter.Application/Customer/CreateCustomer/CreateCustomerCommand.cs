﻿namespace Ship.Ses.Transmitter.Application.Customer.CreateCustomer
{
    public sealed record CreateCustomerCommand(string FullName, DateTime BirthDate, string Email, string Street, string HouseNumber, string FlatNumber, string Country, string PostalCode);
}
