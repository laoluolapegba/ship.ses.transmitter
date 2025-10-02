using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;

    public abstract class FhirSyncRecord
    {
        
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = default!;

        [BsonElement("resourceType")]
        [BsonRepresentation(BsonType.String)]
        public string ResourceType { get; set; } = default!; // e.g., "Patient", "Encounter"

        [BsonElement("resourceId")]
        public string ResourceId { get; set; } = default!;

        // Payload & status
        [BsonElement("fhirJson")]
        public BsonDocument FhirJson { get; set; } = default!;

        [BsonElement("status")]
        public string Status { get; set; } = "Pending"; // Pending, Synced, Failed

        [BsonElement("createdDate")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("timeSynced")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime? TimeSynced { get; set; }

        [BsonElement("retryCount")]
        public int RetryCount { get; set; }

        [BsonElement("errorMessage")]
        public string? ErrorMessage { get; set; }

        [BsonElement("syncedFhirResourceId")]
        public string? SyncedResourceId { get; set; } // Set after successful sync

        [BsonElement("transactionId")]
        public string? TransactionId { get; set; }

        [BsonElement("extractSource")]
        public string? ExtractSource { get; set; } // e.g., "extractor", "api"

        [BsonElement("lastAttemptAt")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime? LastAttemptAt { get; set; }

        [BsonElement("apiResponsePayload")]
        public string? ApiResponsePayload { get; set; } // Raw JSON response from FHIR API

        [BsonElement("facilityId")]
        public string? FacilityId { get; set; }
        [BsonElement("shipService")]
        public string ShipService { get; set; } = default!;

        [BsonElement("stagingId")]
        [BsonRepresentation(BsonType.Int64)]
        public long? StagingId { get; set; }

        // Callback & tracing
        [BsonElement("clientEMRCallbackUrl")]
        public string? ClientEMRCallbackUrl { get; set; }

        [BsonElement("correlationId")]
        public string? CorrelationId { get; set; }

        [BsonElement("clientId")]
        public string ClientId { get; set; } = default!;

        [BsonElement("payloadHash")]
        public string PayloadHash { get; set; } = default!; // SHA-256 of canonical JSON

        // Each concrete resource decides its MongoDB collection
        public abstract string CollectionName { get; }
    }

}
