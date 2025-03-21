using MediatR;
using Serilog.Context;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Patients.Queries;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Patients;
using System.Net;

namespace Ship.Ses.Transmitter.Worker
{
    public class SyncWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SyncWorker> _logger;
        private readonly FhirResourceType _resourceType;

        public SyncWorker(IServiceProvider serviceProvider, ILogger<SyncWorker> logger, FhirResourceType resourceType)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _resourceType = resourceType;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Starting FHIR Sync Worker for {ResourceType}", _resourceType);

            while (!stoppingToken.IsCancellationRequested)
            {
                var correlationId = Guid.NewGuid().ToString();
                using (LogContext.PushProperty("CorrelationId", correlationId))
                using (var scope = _serviceProvider.CreateScope())
                {
                    try
                    {
                        var syncService = scope.ServiceProvider.GetRequiredService<IFhirSyncService>();
                        _logger.LogInformation("🔄 Syncing records for {ResourceType} | CorrelationId: {CorrelationId}", _resourceType, correlationId);
                        await syncService.ProcessPendingRecordsAsync(_resourceType, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing {ResourceType} | CorrelationId: {CorrelationId}", _resourceType, correlationId);
                    }
                }
            
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("🛑 Stopping FHIR Sync Worker for {ResourceType}");
        }

    }
}
