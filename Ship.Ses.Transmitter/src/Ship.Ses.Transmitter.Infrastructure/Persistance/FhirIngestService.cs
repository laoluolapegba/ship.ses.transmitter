using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlX.XDevAPI;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Patients;
using Ship.Ses.Transmitter.Domain.Patients;
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
        private readonly IMongoDatabase _db;
        private readonly ILogger<FhirIngestService> _logger;
        private readonly IClientSyncConfigProvider _clientConfig;
        private const string ExtractSourceApi = "API";
        public FhirIngestService(IMongoClient mongoClient, IOptions<SourceDbSettings> options, 
            ILogger<FhirIngestService> logger,
            IClientSyncConfigProvider clientConfig)
        {
            _db = mongoClient.GetDatabase(options.Value.DatabaseName);
            _logger = logger;
            _clientConfig = clientConfig ;
        }

        public async Task IngestAsync(FhirIngestRequest request, string clientId)
        {
            var bson = BsonDocument.Parse(request.FhirJson.ToJsonString());
            var facilityId = await _clientConfig.GetFacilityIdAsync(clientId);

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
                FacilityId = facilityId

            };

            var collection = _db.GetCollection<PatientSyncRecord>(record.CollectionName); // or use factory
            await collection.InsertOneAsync(record);

            _logger.LogInformation("✅ Ingested {ResourceType} from {Source}", request.ResourceType, clientId);
        }
    }

}
