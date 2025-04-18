using Microsoft.Extensions.Options;
using Serilog.Context;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public class GenericFhirSyncWorker<T> : BackgroundService
        where T : FhirSyncRecord, new()
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<GenericFhirSyncWorker<T>> _logger;
        private readonly IClientSyncConfigProvider _configProvider;
        private readonly string _clientId;

        public GenericFhirSyncWorker(
            IServiceProvider sp,
            IOptions<SeSClientOptions> options,
            ILogger<GenericFhirSyncWorker<T>> logger,
            IClientSyncConfigProvider configProvider)
        {
            _sp = sp;
            _logger = logger;
            _configProvider = configProvider;
            _clientId = options.Value.ClientId;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var resourceType = typeof(T).Name.Replace("SyncRecord", ""); // e.g. "Patient"

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await _configProvider.IsClientActiveAsync(_clientId))
                    {
                        _logger.LogWarning("⛔ Client {ClientId} is not active. Skipping sync for {ResourceType}.", _clientId, resourceType);
                        return;
                    }

                    var enabledResources = await _configProvider.GetEnabledResourcesAsync(_clientId);
                    if (!enabledResources.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("⏸️ {ResourceType} sync is disabled for client {ClientId}.", resourceType, _clientId);
                        return;
                    }

                    using var scope = _sp.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();

                    var correlationId = Guid.NewGuid().ToString();
                    using (LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        var result = await service.ProcessPendingRecordsAsync<T>(stoppingToken);
                        _logger.LogInformation("✅ Synced {ResourceType} records: Total={Total}, Synced={Synced}, Failed={Failed}",
                            resourceType, result.Total, result.Synced, result.Failed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error syncing {ResourceType}", resourceType);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
