using Ship.Ses.Transmitter.Domain.Customers;
using Ship.Ses.Transmitter.Domain.Orders;
using Ship.Ses.Transmitter.Domain.Orders.DomainEvents;
using Ship.Ses.Transmitter.Domain.Orders.Exceptions;

namespace Ship.Ses.Transmitter.Domain.UnitTests
{
    public class OrderTests
    {
        [Fact]
        internal void Should_Create_Order_For_Valid_Input_Data()
        {
            // Arrange
            var customerId = new CustomerId(Guid.NewGuid());
            var shippingAddress = new ShippingAddress("Fifth Avenue 10A", "10037");
            var orderDate = DateTime.UtcNow;

            // Act
            var order = Order.Create(customerId, shippingAddress, orderDate);

            // Assert
            var domainEvents = order.DomainEvents;
            Assert.Single(domainEvents.OfType<OrderCreatedDomainEvent>());
        }

        [Fact]
        internal void Should_Throw_Maximum_Quantity_Exceeded_Domain_Exception_When_Quantity_Is_Higher_Then_5()
        {
            // Arrange
            var customerId = new CustomerId(Guid.NewGuid());
            var shippingAddress = new ShippingAddress("Fifth Avenue 10A", "10037");
            var orderDate = DateTime.UtcNow;

            // Act && Assert
            Assert.Throws<MaximumQuantityExceededDomainException>(() =>
            {
                Order.Create(customerId, shippingAddress, orderDate).AddOrderItem(1, "Tent", 100, "USD", 6);
            });
        }
    }
}