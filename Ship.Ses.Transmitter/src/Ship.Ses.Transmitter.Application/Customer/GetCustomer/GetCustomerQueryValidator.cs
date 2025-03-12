using FluentValidation;

namespace Ship.Ses.Transmitter.Application.Customer.GetCustomer
{
    public class GetCustomerQueryValidator : AbstractValidator<GetCustomerQuery>, IValidator
    {
        public GetCustomerQueryValidator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
        }
    }
}
