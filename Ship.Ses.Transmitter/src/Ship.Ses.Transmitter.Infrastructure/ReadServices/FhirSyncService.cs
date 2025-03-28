using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Services;
using Ship.Ses.Transmitter.Infrastructure.Shared;
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
        private readonly IFhirApiService _fhirApiService;

        public FhirSyncService(IFhirSyncRepository repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
        }

        public async Task ProcessPendingRecordsAsync(FhirResourceType resourceType, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Getting pending records ...");
            var records = await _repository.GetPendingRecordsAsync(resourceType);
            _logger.LogInformation($"Got {records.Count()} pending records from {resourceType.ToString()}...");
            foreach (var record in records) 
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    _logger.LogInformation($"Processing {record.ResourceId}... from {resourceType.ToString()}");
                    // Simulate FHIR API Call
                    //string jsonPayload = record.FhirJson.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
                    string jsonPayload = record.FhirJson.ToCleanJson();
                    await _fhirApiService.SendAsync(
                        FhirOperation.Post,
                        resourceType: "Patient",
                        resourceId: record.ResourceId,
                        jsonPayload: jsonPayload,
                        cancellationToken: cancellationToken
                    );
                    await Task.Delay(1000, cancellationToken);

                    record.Status = "Synced";
                    record.TimeSynced = DateTime.UtcNow;
                    await _repository.UpdateRecordAsync(record);
                    _logger.LogInformation($"Updated resource {record.ResourceId} ..."); 

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
