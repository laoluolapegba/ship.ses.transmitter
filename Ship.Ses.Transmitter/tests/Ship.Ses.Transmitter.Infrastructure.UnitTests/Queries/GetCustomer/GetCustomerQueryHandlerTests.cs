using Ship.Ses.Transmitter.Application.Customer.GetCustomer;
using Ship.Ses.Transmitter.Application.Customer.Shared;
using Ship.Ses.Transmitter.Application.Shared;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MsSql;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Queries.GetCustomer
{
    public class GetCustomerQueryHandlerTests
    {
        private readonly Mock<ICacheService> _cacheServiceMock = new Mock<ICacheService>();
        private readonly Mock<ICustomerReadService> _customerReadService = new Mock<ICustomerReadService>();
        private ServiceProvider _provider;
        private ITestHarness _harness;

        //private Customer _customer = Customer.CreateCustomer(
        //        new CustomerId(Guid.NewGuid()),
        //        new FullName("Mikolaj Jankowski"),
        //        new Age(DateTime.UtcNow.AddYears(-20)),
        //        new Email("my-email@yahoo.com"),
        //        new Address("Fifth Avenue", "10A", "1", "USA", "10037"));

        private CustomerDto _expectedCustomer = new CustomerDto(Guid.NewGuid(), "Mikolaj Jankowski", 35, "mikolaj.jankowski@somedomain.com");

        private void SetupProviderAndHarness()
        {
            _provider = new ServiceCollection()
                .AddMassTransitTestHarness(x => x.AddConsumer<GetCustomerQueryHandler>())
                .AddSingleton(_cacheServiceMock.Object)
                .AddScoped<ICustomerReadService>(_ => _customerReadService.Object)
                //.AddDbContext<IAppDbContext, AppDbContext>(options => options.UseInMemoryDatabase("TestDatabase"))
                .BuildServiceProvider(true);

            _harness = _provider.GetRequiredService<ITestHarness>();
        }

        [Fact]
        public async Task Should_Get_Customer_From_Cache()
        {
            //Arrange

            SetupProviderAndHarness();

            var harness = _provider.GetRequiredService<ITestHarness>();


            _cacheServiceMock
                .Setup(repo => repo.GetAsync<CustomerDto>(Ship.Ses.Transmitter.Application.Shared.CacheKeyBuilder.GetCustomerKey(_expectedCustomer.CustomerId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_expectedCustomer);

            await harness.Start();
            var query = new GetCustomerQuery(_expectedCustomer.CustomerId);


            var client = harness.GetRequestClient<GetCustomerQuery>();

            //Act
            var response = await client.GetResponse<CustomerDto>(query);

            //Assert
            Assert.True(await harness.Sent.Any<CustomerDto>());
            _cacheServiceMock.Verify(repo => repo.GetAsync<CustomerDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            _customerReadService.Verify(repo => repo.GetCustomerById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            Assert.Equal(response.Message, _expectedCustomer);

        }


        [Fact]
        public async Task Should_Get_Customer_From_Db_When_Not_Present_In_Cache()
        {
            //Arrange
            SetupProviderAndHarness();

            var harness = _provider.GetRequiredService<ITestHarness>();



            _cacheServiceMock
                .Setup(repo => repo.GetAsync<CustomerDto?>(Ship.Ses.Transmitter.Application.Shared.CacheKeyBuilder.GetCustomerKey(_expectedCustomer.CustomerId), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CustomerDto?)null);

            _customerReadService
                .Setup(repo => repo.GetCustomerById(_expectedCustomer.CustomerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_expectedCustomer);

            await harness.Start();

            var client = harness.GetRequestClient<GetCustomerQuery>();

            //Act
            var response = await client.GetResponse<CustomerDto>(new GetCustomerQuery(_expectedCustomer.CustomerId));

            //Assert
            Assert.True(await harness.Sent.Any<CustomerDto>());
            _cacheServiceMock.Verify(repo => repo.GetAsync<CustomerDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            _customerReadService.Verify(repo => repo.GetCustomerById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            Assert.Equal(response.Message, _expectedCustomer);

        }

    }
}
