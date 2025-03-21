using Microsoft.Extensions.Logging;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public class FhirSyncService : IFhirSyncService
    {
        private readonly IFhirSyncRepository _repository;
        private readonly ILogger<FhirSyncService> _logger;

        public FhirSyncService(IFhirSyncRepository repository, ILogger<FhirSyncService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task ProcessPendingRecordsAsync(FhirResourceType resourceType, CancellationToken cancellationToken)
        {
            var records = await _repository.GetPendingRecordsAsync(resourceType);

            foreach (var record in records)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    _logger.LogInformation($"Processing {record.ResourceId}... from {resourceType.ToString()}");
                    // Simulate FHIR API Call
                    await Task.Delay(1000, cancellationToken);

                    record.Status = "Synced";
                    record.TimeSynced = DateTime.UtcNow;
                    await _repository.UpdateRecordAsync(record);
                }
                catch (Exception ex)
                {
                    record.RetryCount++;
                    record.ErrorMessage = ex.Message;
                    await _repository.UpdateRecordAsync(record);
                }
            }
        }
    }
}
