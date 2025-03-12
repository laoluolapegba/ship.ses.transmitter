using FluentValidation;

namespace Ship.Ses.Transmitter.Application.Order.GetOrder
{
    public class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>, IValidator
    {
        public GetOrderQueryValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty();
        }
    }
}
