using Ship.Ses.Transmitter.Domain.Orders;
using FluentValidation;

namespace Ship.Ses.Transmitter.Application.Customer.ChangeEmail
{
    public class ChangeEmailCommandValidator : AbstractValidator<ChangeEmailCommand>, IValidator
    {
        public ChangeEmailCommandValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.NewEmail).NotEmpty().MaximumLength(CustomerConstants.Customer.EmailMaxLength);
        }
    }
}
