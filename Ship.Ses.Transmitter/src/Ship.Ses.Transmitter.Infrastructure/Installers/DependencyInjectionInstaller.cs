using Ship.Ses.Transmitter.Application.Shared;
using Ship.Ses.Transmitter.Domain;
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
            //builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            //builder.Services.AddTransient<IDateTimeProvider, DateTimeProvider>();
            //builder.Services.AddScoped<IOrderReadService, OrderReadService>();


        }

    }
}
