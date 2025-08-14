using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Encounter;
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
        //private readonly IClientSyncConfigProvider _configProvider;
        private readonly string _clientId;

        public EncounterSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> clientOptions,
            ILogger<EncounterSyncWorker> logger)
        {
            _sp = sp;
            _logger = logger;
           // _configProvider = configProvider;
            _clientId = clientOptions.Value.ClientId;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Encounter Sync Worker...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var configProvider = scope.ServiceProvider.GetRequiredService<IClientSyncConfigProvider>();

                    if (!await configProvider.IsClientActiveAsync(_clientId))
                    {
                        _logger.LogWarning("⛔ Sync client {ClientId} is not active. Skipping...", _clientId);
                        return;
                    }


                    var enabledResources = await configProvider.GetEnabledResourcesAsync(_clientId);

                    if (!enabledResources.Contains("Encounter", StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("⏸️ Encounter sync disabled for client {ClientId}.", _clientId);
                        return;
                    }

                    var syncService = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                    var correlationId = Guid.NewGuid().ToString();
                    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        var result = await syncService.ProcessPendingRecordsAsync<EncounterSyncRecord>(stoppingToken);

                        _logger.LogInformation("✅ Synced Encounter records: Total={Total}, Synced={Synced}, Failed={Failed}",
                            result.Total, result.Synced, result.Failed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Unhandled exception in EncounterSyncWorker");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 Encounter Sync Worker stopped.");
        }
    }
}