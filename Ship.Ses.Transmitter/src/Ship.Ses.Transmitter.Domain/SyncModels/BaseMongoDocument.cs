using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.SyncModels
{
    public abstract class BaseMongoDocument
    {
        // Stored as an ObjectId in Mongo but exposed as an opaque string so the persistence contract
        // (IFhirSyncStore) stays storage-neutral (same pattern as FhirSyncRecord.Id).
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = default!;

        public abstract string CollectionName { get; }
    }
}
