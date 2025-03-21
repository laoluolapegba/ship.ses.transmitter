using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Ship.Ses.Transmitter.Domain.Patients.PatientAggregate
{
    public class OutboxRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("resourceType")]
        public string ResourceType { get; set; } = "Patient";

        [BsonElement("resourceId")]
        public string ResourceId { get; set; }

        [BsonElement("data")]
        public string Data { get; set; } // Store serialized patient record

        [BsonElement("status")]
        public string Status { get; set; } = "Pending"; // Pending, Synced, Failed

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }
}
