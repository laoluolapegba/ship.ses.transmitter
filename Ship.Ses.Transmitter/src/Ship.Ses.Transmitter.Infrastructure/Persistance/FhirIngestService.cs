using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlX.XDevAPI;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Patients;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance
{
    public class FhirIngestService : IFhirIngestService
    {
        private readonly IMongoSyncRepository _mongoSyncRepository;
        private readonly ILogger<FhirIngestService> _logger;
        private readonly IClientSyncConfigProvider _clientConfig;
        private const string ExtractSourceApi = "API";
        public FhirIngestService(IMongoSyncRepository mongoSyncRepository, IOptions<SourceDbSettings> options, 
            ILogger<FhirIngestService> logger,
            IClientSyncConfigProvider clientConfig)
        {
            _mongoSyncRepository = mongoSyncRepository;
            _logger = logger;
            _clientConfig = clientConfig ;
        }

        public async Task IngestAsync(FhirIngestRequest request, string clientId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Client ID cannot be null or empty.", nameof(clientId));

            var bson = BsonDocument.Parse(request.FhirJson.ToJsonString());
            //var facilityId = await _clientConfig.GetFacilityIdAsync(clientId);

            var record = new PatientSyncRecord  // TODO: Dynamically resolve by resourceType
            {
                ResourceType = request.ResourceType,
                ResourceId = request.ResourceId,
                FhirJson = bson,
                CreatedDate = DateTime.UtcNow,
                Status = "Pending",
                RetryCount = 0,
                ExtractSource = ExtractSourceApi,
                TransactionId = null,
                ApiResponsePayload = null, // Initially null, will be updated after sync
                LastAttemptAt = null,
                SyncedResourceId = null, // Initially null, will be updated after sync
                FacilityId = request.FacilityId

            };

            await _mongoSyncRepository.AddRecordAsync(record);

            _logger.LogInformation(" Ingested {ResourceType} from {Source}", request.ResourceType, clientId);
        }
    }

}
