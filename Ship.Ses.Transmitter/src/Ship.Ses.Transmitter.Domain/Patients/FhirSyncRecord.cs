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
        // Derived classes must override collection name
        public abstract string CollectionName { get; }

    }
}
