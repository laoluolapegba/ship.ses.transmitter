using Ship.Ses.Transmitter.Application.Customer.CreateCustomer;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Infrastructure.Filters.MassTransit;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using MassTransit;
using MassTransit.Internals;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class MassTransitInstaller
    {
        public static void InstallMassTransit(this WebApplicationBuilder builder)
        {
            var rabbitMqSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>()!.RabbitMq;

            builder.Services.AddMediator(cfg =>
            {
                AddMediatorConsumersFromAssembly(cfg);


                cfg.ConfigureMediator((context, cfg) =>
                {
                    //The order of filter registration matters.

                    cfg.UseConsumeFilter(typeof(ValidationFilter<>), context, x => x.Include(type => !type.HasInterface<IDomainEvent>()));
                    cfg.UseConsumeFilter(typeof(LoggingFilter<>), context, x => x.Include(type => !type.HasInterface<IDomainEvent>()));
                    cfg.UseConsumeFilter(typeof(RedisFilter<>), context, x => x.Include(type => !type.HasInterface<IDomainEvent>()));
                    cfg.UseConsumeFilter(typeof(EventsFilter<>), context, x => x.Include(type => !type.HasInterface<IDomainEvent>()));
                    cfg.UseConsumeFilter(typeof(HtmlSanitizerFilter<>), context, x => x.Include(type => !type.HasInterface<IDomainEvent>()));


                    //cfg.UseConsumeFilter<GetCustomerQueryCacheFilter>(context);


                    //cfg.UseMessageRetry(x => x.Interval(3, TimeSpan.FromSeconds(15))); //causes long response to HTTP requests
                });
            });

            builder.Services.AddMassTransit(x =>
            {
                //below Consumers for RabbitMq
                x.AddConsumer<CustomerCreatedIntegrationEventHandler>();

                x.SetKebabCaseEndpointNameFormatter();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(rabbitMqSettings.Host);

                    cfg.ConfigureEndpoints(context);
                });
            });
        }

        private static void AddMediatorConsumersFromAssembly(IMediatorRegistrationConfigurator cfg)
        {
            cfg.AddConsumers(typeof(CreateCustomerCommandHandler).Assembly);
        }
    }

}
