using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public abstract class FhirSyncRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("resourceType")]
        [BsonRepresentation(BsonType.String)] // Explicitly store enum as a string
        //public FhirResourceType ResourceType { get; set; }
        public string ResourceType { get; set; }

        [BsonElement("resourceId")]
        public string ResourceId { get; set; }

        [BsonElement("fhirJson")] // Explicitly maps to "fhirJson" in MongoDB
        public BsonDocument FhirJson { get; set; }

        [BsonElement("status")]
        public string Status { get; set; } // Pending, Synced, Failed

        [BsonElement("createdDate")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreatedDate { get; set; }

        [BsonElement("timeSynced")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime? TimeSynced { get; set; }

        [BsonElement("retryCount")]
        public int RetryCount { get; set; }

        [BsonElement("errorMessage")]
        public string ErrorMessage { get; set; }

        [BsonElement("syncedFhirResourceId")]
        public string SyncedResourceId { get; set; } // Updated after sync
        [BsonElement("transactionId")]
        public string TransactionId { get; set; }

        [BsonElement("extractSource")]
        public string ExtractSource { get; set; } // Source of the extraction (e.g., "extractor", "api")
        [BsonElement("lastAttemptAt")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime? LastAttemptAt { get; set; } // Last attempt time for sync
        [BsonElement("apiResponsePayload")]
        public string ApiResponsePayload { get; set; }  // Raw JSON response from FHIR API
        [BsonElement("facilityId")]
        public string FacilityId { get; set; }
        // Derived classes must override collection name
        public abstract string CollectionName { get; }
        [BsonElement("stagingId")]
        [BsonRepresentation(BsonType.Int64)]
        public long? StagingId { get; set; }

        [BsonElement("clientEMRCallbackUrl")]
        public string? ClientEMRCallbackUrl { get; set; }

        [BsonElement("shipService")]
        public string? ShipService { get; set; }
    }
}
