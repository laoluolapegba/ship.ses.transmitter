using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain
{
    public class FhirSyncRepository : IFhirSyncRepository
    {
        private readonly IMongoCollection<FhirSyncRecord> _collection;

        public FhirSyncRepository(IOptions<SourceDbSettings> settings, IMongoClient client)
   : this(client.GetDatabase(settings.Value.DatabaseName), settings.Value.CollectionName)
        {
        }

        public FhirSyncRepository(IMongoDatabase database, string collectionName)
        {
            _collection = database.GetCollection<FhirSyncRecord>(collectionName);
        }
        //public FhirSyncRepository(IMongoCollection<FhirSyncRecord> settings) //IOptions<SourceDbSettings> settings)
        //{
        //    var client = new MongoClient(settings.Value.ConnectionString);
        //    var database = client.GetDatabase(settings.Value.DatabaseName);
        //    _collection = database.GetCollection<FhirSyncRecord>(settings.Value.CollectionName);
        //}

        public async Task<IEnumerable<FhirSyncRecord>> GetPendingRecordsAsync(FhirResourceType resourceType)
        {
            var filter = Builders<FhirSyncRecord>.Filter.And(
                Builders<FhirSyncRecord>.Filter.Eq(r => r.ResourceType, resourceType),
                Builders<FhirSyncRecord>.Filter.Eq(r => r.Status, "Pending")
            );
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task AddRecordAsync(FhirSyncRecord record)
        {
            await _collection.InsertOneAsync(record);
        }

        public async Task UpdateRecordAsync(FhirSyncRecord record)
        {
            var filter = Builders<FhirSyncRecord>.Filter.Eq(r => r.Id, record.Id);
            await _collection.ReplaceOneAsync(filter, record);
        }
    }
}
