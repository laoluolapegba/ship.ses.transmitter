﻿using Microsoft.Extensions.Options;
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
    /// <summary>
    /// Generic repository for interacting with MongoDB synchronization records.
    /// </summary>
    public class MongoSyncRepository : IMongoSyncRepository
    {
        private readonly IMongoDatabase _database;

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoSyncRepository"/> class.
        /// </summary>
        /// <param name="settings">The database settings from configuration.</param>
        /// <param name="client">The MongoDB client.</param>
        public MongoSyncRepository(IOptions<SourceDbSettings> settings, IMongoClient client)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.Value.DatabaseName))
            {
                throw new ArgumentException("SourceDbSettings or DatabaseName is not configured for MongoSyncRepository.", nameof(settings));
            }
            _database = client.GetDatabase(settings.Value.DatabaseName);
        }

        //public MongoSyncRepository(IOptions<SourceDbSettings> settings, IMongoClient client)
        //{
        //    _database = client.GetDatabase(settings.Value.DatabaseName);
        //}

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

        public async Task BulkUpdateStatusAsync<T>(
    Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)> updates
) where T : FhirSyncRecord, new()
        {
            var collection = _database.GetCollection<T>(new T().CollectionName);

            var models = updates.Select(kv =>
            {
                var filter = Builders<T>.Filter.Eq(r => r.Id, kv.Key.ToString());
                var update = Builders<T>.Update
                    .Set(r => r.Status, kv.Value.status)
                    .Set(r => r.ErrorMessage, kv.Value.message)
                    .Set(r => r.TimeSynced, DateTime.UtcNow)
                    .Set(r => r.TransactionId, kv.Value.transactionId)
                    .Set(r => r.ApiResponsePayload, kv.Value.rawResponse)
                    .Set(r => r.LastAttemptAt, DateTime.UtcNow);

                return new UpdateOneModel<T>(filter, update);
            });

            await collection.BulkWriteAsync(models);
        }

        
    }
}
