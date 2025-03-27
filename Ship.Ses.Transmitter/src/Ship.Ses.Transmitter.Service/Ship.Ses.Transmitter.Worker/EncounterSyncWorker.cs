using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public class EncounterSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<EncounterSyncWorker> _logger;
        private readonly ResourceSyncSettings _settings;

        public EncounterSyncWorker(IServiceProvider sp, IOptions<SyncOptions> options, ILogger<EncounterSyncWorker> logger)
        {
            _sp = sp;
            _logger = logger;
            _settings = options.Value.Encounter;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("⏸️ Encounter sync disabled in config.");
                return;
            }

            _logger.LogInformation("🚀 Starting Encounter Sync Worker...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var repo = scope.ServiceProvider
                                    .GetRequiredService<IFhirSyncRepositoryFactory>()
                                    .Create(_settings.CollectionName);

                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<FhirSyncService>>();
                    var service = new FhirSyncService(repo, logger);

                    var correlationId = Guid.NewGuid().ToString();
                    using (LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        await service.ProcessPendingRecordsAsync(FhirResourceType.Encounter, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in EncounterSyncWorker");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // backoff
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 Encounter Sync Worker stopped.");
        }
    }
}