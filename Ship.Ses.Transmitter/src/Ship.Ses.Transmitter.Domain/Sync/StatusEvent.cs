using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Ship.Ses.Transmitter.Domain.SyncModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    public sealed class StatusEvent : BaseMongoDocument
    {
        [BsonElement("transactionId")]
        public string TransactionId { get; set; } = default!;

        [BsonElement("resourceType")]
        public string ResourceType { get; set; } = "Patient";

        [BsonElement("resourceId")]
        public string? ResourceId { get; set; }

        [BsonElement("shipId")]
        public string ShipId { get; set; } = default!;

        [BsonElement("status")]
        public string Status { get; set; } = default!;           // SUCCESS|FAILED|PENDING

        [BsonElement("message")]
        public string Message { get; set; } = default!;

        [BsonElement("receivedAtUtc")]
        public DateTime ReceivedAtUtc { get; set; }

        [BsonElement("source")]
        public string Source { get; set; } = "SHIP";

        [BsonElement("headers")]
        public string? Headers { get; set; }

        [BsonElement("payloadHash")]
        public string PayloadHash { get; set; } = default!;

        [BsonElement("data")]
        public BsonDocument? Data { get; set; }                  // full Patient payload from SHIP
        [BsonElement("correlationId")]
        public string CorrelationId { get; set; }

        // Outbox fields for EMR callback processing
        [BsonElement("callbackStatus")]
        public string CallbackStatus { get; set; } = "Pending";  // Pending|InFlight|Succeeded|Failed

        [BsonElement("callbackAttempts")]
        public int CallbackAttempts { get; set; }

        [BsonElement("callbackNextAttemptAt")]
        public DateTime? CallbackNextAttemptAt { get; set; } = DateTime.UtcNow;

        [BsonElement("callbackLastError")]
        public string? CallbackLastError { get; set; }

        [BsonElement("callbackDeliveredAt")]
        public DateTime? CallbackDeliveredAt { get; set; }

        [BsonElement("emrTargetUrl")]
        public string? EmrTargetUrl { get; set; }                // optional cache (we’ll read from PatientSyncRecord)

        [BsonElement("emrResponseStatusCode")]
        public int? EmrResponseStatusCode { get; set; }

        [BsonElement("emrResponseBody")]
        public string? EmrResponseBody { get; set; }

        public override string CollectionName => "patientstatusevents";
    }

}
