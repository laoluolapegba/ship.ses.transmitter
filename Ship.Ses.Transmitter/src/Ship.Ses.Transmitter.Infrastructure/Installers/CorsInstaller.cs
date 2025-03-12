using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class CorsInstaller
    {
        public static string DefaultCorsPolicyName = "AllowSpecificOrigins";
        public static void InstallCors(this WebApplicationBuilder builder)
        {
            var cors = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>()!.Cors;

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName,
                    corsBuilder =>
                    {
                        corsBuilder.WithOrigins(cors.AllowedOrigins)
                         .WithMethods(cors.AllowedMethods)
                         .WithHeaders(cors.AllowedHeaders);
                    });
            });

        }
    }
}
