using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker.Installers
{
    public static class SyncWorkerServiceCollectionExtensions
    {
        //public static IServiceCollection AddFhirResourceSyncWorker<TRecord>(
        //    this IServiceCollection services, string resourceName)
        //    where TRecord : FhirSyncRecord
        //{
        //    services.AddHostedService(sp =>
        //    {
        //        var logger = sp.GetRequiredService<ILogger<ResourceSyncWorker<TRecord>>>();
        //        var opts = sp.GetRequiredService<IOptions<SeSClientOptions>>();
        //        return new ResourceSyncWorker<TRecord>(sp, opts, logger, resourceName);
        //    });
        //    return services;
        //}
    }

}
