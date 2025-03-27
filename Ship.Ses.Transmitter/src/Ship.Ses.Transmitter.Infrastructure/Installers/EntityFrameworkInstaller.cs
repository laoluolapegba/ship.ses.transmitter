using Ship.Ses.Transmitter.Infrastructure.Persistance.MsSql;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class EntityFrameworkInstaller
    {
        public static void InstallEntityFramework(this WebApplicationBuilder builder)
        {
            var appSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();
            if (appSettings != null)
            {
                var msSqlSettings = appSettings.ShipServerSqlDb;
                builder.Services.AddDbContext<AppDbContext>(options => options.UseMySQL(msSqlSettings.ConnectionString));
                builder.Services.AddScoped<IAppDbContext>(provider => provider.GetService<AppDbContext>());
            }
        }

        public static void SeedDatabase(AppDbContext appDbContext)
        {
            appDbContext.Database.Migrate();
        }
        
    }


}
