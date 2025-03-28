using Microsoft.Extensions.Options;
using Serilog.Context;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public class PatientSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<PatientSyncWorker> _logger;
        private readonly ResourceSyncSettings _settings;

        public PatientSyncWorker(IServiceProvider sp, IOptions<SyncOptions> options, ILogger<PatientSyncWorker> logger)
        {
            _sp = sp;
            _logger = logger;
            _settings = options.Value.Patient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("⏸️ Patient sync disabled in config.");
                return;
            }

            _logger.LogInformation("🚀 Starting Patient Sync Worker...");

            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    using var scope = _sp.CreateScope();
            //    var repo = scope.ServiceProvider.GetRequiredService<IFhirSyncRepositoryFactory>()
            //                    .Create(_settings.CollectionName);
            //    _logger.LogInformation("🚀 collection... {_settings.CollectionName}");
            //    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FhirSyncService>>();
            //    var service = new FhirSyncService(repo, logger);

            //    await service.ProcessPendingRecordsAsync(FhirResourceType.Patient, stoppingToken);
            //    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            //}

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var repo = scope.ServiceProvider
                        .GetRequiredService<IFhirSyncRepositoryFactory>()
                        .Create(_settings.CollectionName);

                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FhirSyncService>>();
                    //var service = new FhirSyncService(repo, logger);
                    using var syncScope = _sp.CreateScope();
                    var service = syncScope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                    var correlationId = Guid.NewGuid().ToString();
                    using (LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        await service.ProcessPendingRecordsAsync(FhirResourceType.Patient, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in {WorkerName}", nameof(PatientSyncWorker));
                    // Optional: delay to avoid tight retry loop
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

}
