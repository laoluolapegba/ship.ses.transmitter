using Ship.Ses.Transmitter.Application.Shared;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Customers;
using Ship.Ses.Transmitter.Domain.Customers.DomainEvents;
using Ship.Ses.Transmitter.Domain.Orders;
using Ship.Ses.Transmitter.Infrastructure.Authentication;
using Ship.Ses.Transmitter.Infrastructure.BackgroundTasks;
using Ship.Ses.Transmitter.Infrastructure.Events;
using Ship.Ses.Transmitter.Infrastructure.Exceptions;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Customers;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Orders;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Shared;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class DependencyInjectionInstaller
    {
        public static void InstallDependencyInjectionRegistrations(this WebApplicationBuilder builder)
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
            builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            builder.Services.AddTransient<IDateTimeProvider, DateTimeProvider>();
            builder.Services.AddTransient<IDomainEventDispatcher, DomainEventDispatcher>();
            builder.Services.AddHostedService<DomainEventsProcessor>();
            builder.Services.AddHostedService<IntegrationEventsProcessor>();

            builder.Services.AddTransient<CustomerCreatedEventMapper>();
            builder.Services.AddSingleton<EventMapperFactory>(provider =>
            {
                var mappers = new Dictionary<Type, IEventMapper>
                {
                    { typeof(CustomerCreatedDomainEvent), provider.GetRequiredService<CustomerCreatedEventMapper>() },
                };

                return new EventMapperFactory(mappers);
            });
            builder.Services.AddValidatorsFromAssemblyContaining<IApplicationValidator>(ServiceLifetime.Transient);
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<CommandValidationExceptionHandler>();
            builder.Services.AddSingleton<ICacheService, CacheService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IEmailTemplateFactory, EmailTemplateFactory>();
            builder.Services.AddScoped<OrderDomainService>();
            builder.Services.AddScoped<IOrderReadService, OrderReadService>();
            builder.Services.AddScoped<ICustomerReadService, CustomerReadService>();
            builder.Services.AddHttpClient<IAuthenticationService, KeycloakAuthenticationService>();

        }

    }
}
