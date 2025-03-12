using Ship.Ses.Transmitter.Domain.Orders.DomainEvents;
using MassTransit;

namespace Ship.Ses.Transmitter.Application.Order.CreateOrder.DomainEventHandlers
{
    public sealed class OrderCreatedDomainEventHandler : IConsumer<OrderCreatedDomainEvent>
    {

        public OrderCreatedDomainEventHandler()
        {
        }
        public async Task Consume(ConsumeContext<OrderCreatedDomainEvent> context)
        {
            //Sending e-mail.
        }
    }
}
