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

           
        }

    }
}
