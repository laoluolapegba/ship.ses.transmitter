using Microsoft.Extensions.Options;
using MongoDB.Bson;
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
        private readonly IMongoDatabase _database;

        public FhirSyncRepository(IOptions<SourceDbSettings> settings, IMongoClient client)
        {
            _database = client.GetDatabase(settings.Value.DatabaseName);
        }

        public async Task<IEnumerable<T>> GetPendingRecordsAsync<T>() where T : FhirSyncRecord, new()
        {
            var collectionName = new T().CollectionName;
            var collection = _database.GetCollection<T>(collectionName);

            var filter = Builders<T>.Filter.Eq(r => r.Status, "Pending");
            return await collection.Find(filter).ToListAsync();
        }

        public async Task AddRecordAsync<T>(T record) where T : FhirSyncRecord
        {
            var collection = _database.GetCollection<T>(record.CollectionName);
            await collection.InsertOneAsync(record);
        }

        public async Task UpdateRecordAsync<T>(T record) where T : FhirSyncRecord
        {
            var collection = _database.GetCollection<T>(record.CollectionName);
            var filter = Builders<T>.Filter.Eq(r => r.Id, record.Id);
            await collection.ReplaceOneAsync(filter, record);
        }
        public async Task<IEnumerable<T>> GetByStatusAsync<T>(string status, int skip = 0, int take = 100)
    where T : FhirSyncRecord, new()
        {
            var collection = _database.GetCollection<T>(new T().CollectionName);
            var filter = Builders<T>.Filter.Eq(r => r.Status, status);
            return await collection.Find(filter)
                .Skip(skip)
                .Limit(take)
                .ToListAsync();
        }

        public async Task BulkUpdateStatusAsync<T>(IEnumerable<ObjectId> ids, string status, string error = null)
            where T : FhirSyncRecord, new()
        {
            var collection = _database.GetCollection<T>(new T().CollectionName);
            var filter = Builders<T>.Filter.In("Id", ids);

            var update = Builders<T>.Update
                .Set(r => r.Status, status)
                .Set(r => r.TimeSynced, DateTime.UtcNow);

            if (!string.IsNullOrEmpty(error))
                update = update.Set(r => r.ErrorMessage, error);

            await collection.UpdateManyAsync(filter, update);
        }
    }
}
