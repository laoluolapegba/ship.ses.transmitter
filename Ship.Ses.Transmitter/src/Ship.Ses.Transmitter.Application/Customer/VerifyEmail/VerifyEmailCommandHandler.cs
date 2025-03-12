using Ship.Ses.Transmitter.Application.Exceptions;
using Ship.Ses.Transmitter.Domain.Customers;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Ship.Ses.Transmitter.Application.Customer.VerifyEmail;

public sealed class VerifyEmailCommandHandler : IConsumer<VerifyEmailCommand>
{
    private readonly ICustomerRepository _customerRespository;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(ICustomerRepository customerRespository, ILogger<VerifyEmailCommandHandler> logger)
    {
        _customerRespository = customerRespository;
        _logger = logger;
    }


    public async Task Consume(ConsumeContext<VerifyEmailCommand> command)
    {
        var customerId = command.Message.CustomerId;
        var customer = await _customerRespository.GetAsync(customerId, command.CancellationToken);

        if (customer == null)
        {
            throw new CustomerNotFoundApplicationException(customerId);
        }

        if (customer.IsEmailVerified)
        {
            throw new EmailAlreadyVerifiedApplicationException(customer.Email.Value);
        }

        customer.VerifyEmailAddress();

        _logger.LogInformation("Email address for customer '{email}' has been verified", customerId);

    }

}
