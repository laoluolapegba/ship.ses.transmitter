using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
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

        /// <summary>
        /// Find the originating patient record by transaction id
        /// </summary>
        /// <param name="transactionId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>

        public async Task<PatientSyncRecord?> GetPatientByTransactionIdAsync(string transactionId, CancellationToken ct = default)
        {
            var col = _database.GetCollection<PatientSyncRecord>("transformed_pool_patients");
            var filter = Builders<PatientSyncRecord>.Filter.Eq("transactionId", transactionId);
            return await col.Find(filter).FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Fetch due events from patientstatusevents
        /// </summary>
        /// <param name="batchSize"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<List<StatusEvent>> FetchDueEmrCallbacksAsync(int batchSize, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("patientstatusevents");
            var now = DateTime.UtcNow;

            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Ne(x => x.CallbackStatus, "Succeeded"),
                Builders<StatusEvent>.Filter.Lte(x => x.CallbackNextAttemptAt, now)
            );

            return await col.Find(filter)
                            .Sort(Builders<StatusEvent>.Sort.Ascending(x => x.CallbackNextAttemptAt))
                            .Limit(batchSize)
                            .ToListAsync(ct);
        }

        /// <summary>
        /// Try to atomically mark as InFlight (claim)
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<bool> TryMarkInFlightAsync(ObjectId id, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("patientstatusevents");
            var now = DateTime.UtcNow;

            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Eq(x => x.Id, id),
                Builders<StatusEvent>.Filter.Ne(x => x.CallbackStatus, "InFlight"),
                Builders<StatusEvent>.Filter.Ne(x => x.CallbackStatus, "Succeeded"),
                Builders<StatusEvent>.Filter.Lte(x => x.CallbackNextAttemptAt, now)
            );

            var update = Builders<StatusEvent>.Update
                .Set(x => x.CallbackStatus, "InFlight")
                .Set(x => x.CallbackLastError, null);

            var res = await col.UpdateOneAsync(filter, update, cancellationToken: ct);
            return res.ModifiedCount == 1;
        }

        /// <summary>
        /// Mark delivery success
        /// </summary>
        /// <param name="id"></param>
        /// <param name="statusCode"></param>
        /// <param name="body"></param>
        /// <param name="targetUrl"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task MarkEmrCallbackSucceededAsync(ObjectId id, int statusCode, string? body, string? targetUrl, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("patientstatusevents");
            var update = Builders<StatusEvent>.Update
                .Set(x => x.CallbackStatus, "Succeeded")
                .Set(x => x.CallbackDeliveredAt, DateTime.UtcNow)
                .Set(x => x.EmrResponseStatusCode, statusCode)
                .Set(x => x.EmrResponseBody, Truncate(body, 4000))
                .Set(x => x.EmrTargetUrl, targetUrl);
            await col.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        /// <summary>
        /// Mark for retry with backoff
        /// </summary>
        /// <param name="id"></param>
        /// <param name="error"></param>
        /// <param name="delay"></param>
        /// <param name="targetUrl"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task MarkEmrCallbackRetryAsync(ObjectId id, string? error, TimeSpan delay, string? targetUrl, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("patientstatusevents");
            var update = Builders<StatusEvent>.Update
                .Inc(x => x.CallbackAttempts, 1)
                .Set(x => x.CallbackStatus, "Pending")
                .Set(x => x.CallbackLastError, Truncate(error, 2000))
                .Set(x => x.CallbackNextAttemptAt, DateTime.UtcNow.Add(delay))
                .Set(x => x.EmrTargetUrl, targetUrl);
            await col.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        private static string? Truncate(string? s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

    }
}
