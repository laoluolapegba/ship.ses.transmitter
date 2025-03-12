using Ship.Ses.Transmitter.Domain.Customers;
using Ship.Ses.Transmitter.Domain.Orders.DomainEvents;
using Ship.Ses.Transmitter.Domain.Orders.Exceptions;
using Ship.Ses.Transmitter.Domain.Orders.Policies;

namespace Ship.Ses.Transmitter.Domain.Orders
{
    public class Order : Entity
    {
        public OrderId OrderId { get; private set; }
        public ShippingAddress ShippingAddress { get; private set; }
        public CustomerId CustomerId { get; private set; }

        public DateTime OrderDate { get; private set; }

        private readonly List<OrderItem> _orderItems;
        public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

        public Money TotalAmount { get; internal set; } = new Money(0);
        public Discount Discount { get; internal set; }
        private Order()
        {

        }

        public static Order Create(
            CustomerId customerId,
            ShippingAddress shippingAddress,
            DateTime orderDate)
        {
            return new Order(customerId, shippingAddress, orderDate);
        }



        private Order(CustomerId customerId, ShippingAddress shippingAddress, DateTime orderDate)
        {
            CustomerId = customerId;
            OrderId = new OrderId(Guid.NewGuid());
            ShippingAddress = shippingAddress;
            OrderDate = orderDate;

            _orderItems = new List<OrderItem>();

            AddDomainEvent(new OrderCreatedDomainEvent(this.OrderId.Value, this.CustomerId.Value));
        }
        private decimal ApplyDiscount()
        {
            var discount = new AmountBasedDiscountPolicy().CalculateDiscount(_orderItems);
            return discount;
        }

        public void AddOrderItem(long productId, string productName, decimal price, string currency, uint quantity = 1)
        {
            if (quantity > OrderConstants.Order.MaxQuantity)
            {
                throw new MaximumQuantityExceededDomainException();
            }

            var orderItem = OrderItem.Create(this.OrderId, productId, productName, price, currency, quantity);
            _orderItems.Add(orderItem);
            CalculateTotalAmount();
        }
        private Money CalculateTotalAmount()
        {
            var discount = ApplyDiscount();
            var orderAmount = _orderItems.Sum(x => x.Quantity * x.Price.Amount);

            if (discount > 0)
            {
                var discountAmount = orderAmount * discount;
                Discount = new Discount(discountAmount, DiscountType.OrderBasedAmount);
                TotalAmount = new Money(orderAmount - discountAmount);
                return TotalAmount;
            }
            else
            {
                TotalAmount = new Money(orderAmount);
                return TotalAmount;
            }
        }
    }
}
