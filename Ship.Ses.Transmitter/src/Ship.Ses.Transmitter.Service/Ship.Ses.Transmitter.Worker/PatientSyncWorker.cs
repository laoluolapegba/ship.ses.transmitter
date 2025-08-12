using Microsoft.Extensions.DependencyInjection;
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
        //private readonly IClientSyncConfigProvider _configProvider;
        private readonly string _clientId;

        public PatientSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> clientOptions,
            ILogger<PatientSyncWorker> logger)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientId = clientOptions?.Value.ClientId ?? throw new ArgumentNullException(nameof(clientOptions));

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //_logger.LogInformation("🚀 PatientSyncWorker: Entered ExecuteAsync");
            _logger.LogInformation("Starting Patient Sync Worker...");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("PatientSyncWorker: Beginning sync loop");
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

                    if (!enabledResources.Contains("Patient", StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("⏸️ Patient sync disabled for client {ClientId}.", _clientId);
                        return;
                    }

                    var syncService = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                    var correlationId = Guid.NewGuid().ToString();
                    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        var result = await syncService.ProcessPendingRecordsAsync<PatientSyncRecord>(stoppingToken);

                        _logger.LogInformation("Synced Patient records: Total={Total}, Synced={Synced}, Failed={Failed}",
                            result.Total, result.Synced, result.Failed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Unhandled exception in PatientSyncWorker");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 Patient Sync Worker stopped.");
        }
    }

}
