using FluentValidation;

namespace Ship.Ses.Transmitter.Application.Customer.VerifyEmail
{
    public class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>, IValidator
    {
        public VerifyEmailCommandValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
        }
    }
}
