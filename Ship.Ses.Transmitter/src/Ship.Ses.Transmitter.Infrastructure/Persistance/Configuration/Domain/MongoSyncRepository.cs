using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Application.Interfaces;
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
    /// MongoDB adapter for <see cref="IFhirSyncStore"/>. All MongoDB specifics (ObjectId representation,
    /// BsonDocument payloads, collection-per-resource) are confined to this class; callers see only the
    /// storage-neutral contract. A PostgreSQL adapter can replace this without touching workers/services.
    /// </summary>
    public class MongoSyncRepository : IFhirSyncStore
    {
        private readonly IMongoDatabase _database;
        private readonly ILogger<MongoSyncRepository> _logger;
        private IMongoCollection<StatusEvent> StatusEventCol =>
            _database.GetCollection<StatusEvent>("fhirstatusevents");

        public MongoSyncRepository(IOptions<SourceDbSettings> settings, IMongoClient client, ILogger<MongoSyncRepository> logger)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.Value.DatabaseName))
            {
                throw new ArgumentException("SourceDbSettings or DatabaseName is not configured for MongoSyncRepository.", nameof(settings));
            }
            _database = client.GetDatabase(settings.Value.DatabaseName);
            _logger = logger;
        }

        public async Task<IEnumerable<T>> GetPendingRecordsAsync<T>() where T : FhirSyncRecord, new()
        {
            var collection = _database.GetCollection<T>(new T().CollectionName);
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

        public async Task BulkUpdateStatusAsync<T>(IReadOnlyDictionary<string, RecordStatusUpdate> updates)
            where T : FhirSyncRecord, new()
        {
            var collection = _database.GetCollection<T>(new T().CollectionName);

            var models = updates.Select(kv =>
            {
                var filter = Builders<T>.Filter.Eq(r => r.Id, kv.Key);
                var v = kv.Value;
                var update = Builders<T>.Update
                    .Set(r => r.Status, v.Status)
                    .Set(r => r.ErrorMessage, v.Message)
                    .Set(r => r.TransactionId, v.TransactionId)
                    .Set(r => r.ApiResponsePayload, v.RawResponse)
                    .Set(r => r.LastAttemptAt, DateTime.UtcNow);

                // TimeSynced reflects a successful sync only — not a failed attempt or a retry requeue.
                if (string.Equals(v.Status, "Synced", StringComparison.OrdinalIgnoreCase))
                    update = update.Set(r => r.TimeSynced, DateTime.UtcNow);

                // Count every failed attempt (requeue or permanent fail) so bounded retry can cap it (Finding 3.5).
                if (v.IncrementRetry)
                    update = update.Inc(r => r.RetryCount, 1);

                return new UpdateOneModel<T>(filter, update);
            });

            await collection.BulkWriteAsync(models);
        }

        public async Task<PatientSyncRecord?> GetPatientByTransactionIdAsync(string transactionId, CancellationToken ct = default)
        {
            var col = _database.GetCollection<PatientSyncRecord>("transformed_pool_patients");
            var filter = Builders<PatientSyncRecord>.Filter.Eq("transactionId", transactionId);
            return await col.Find(filter).FirstOrDefaultAsync(ct);
        }

        public Task InsertStatusEventAsync(StatusEvent ev, CancellationToken ct = default)
            => StatusEventCol.InsertOneAsync(ev, cancellationToken: ct);

        // Fixed name so re-running at startup is a no-op instead of creating a second index.
        private const string TransactionIdUniqueIndexName = "ux_fhirstatusevents_transactionId";

        public async Task EnsureStatusEventSchemaAsync(CancellationToken ct = default)
        {
            // Unique on transactionId so the probe flow and the real SHIP callback converge on ONE document
            // per transaction. Partial (non-empty transactionId only) because hard-failure ERROR events carry
            // no transactionId and must not collide with each other on "".
            var keys = Builders<StatusEvent>.IndexKeys.Ascending(x => x.TransactionId);
            var options = new CreateIndexOptions<StatusEvent>
            {
                Name = TransactionIdUniqueIndexName,
                Unique = true,
                // partialFilterExpression: transactionId is a non-empty string ($gt "" excludes missing/null/empty).
                PartialFilterExpression = new BsonDocument("transactionId", new BsonDocument("$gt", ""))
            };

            try
            {
                await StatusEventCol.Indexes.CreateOneAsync(new CreateIndexModel<StatusEvent>(keys, options), cancellationToken: ct);
                _logger.LogInformation("🧭 Ensured unique index '{Index}' on fhirstatusevents.transactionId (partial, non-empty).",
                    TransactionIdUniqueIndexName);
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" || ex.Code == 85 || ex.Code == 86)
            {
                // An index on transactionId already exists with different options/name — leave it in place.
                _logger.LogWarning(ex, "🧭 A transactionId index already exists on fhirstatusevents with different options; leaving it unchanged.");
            }
            catch (Exception ex)
            {
                // Most likely: legacy DUPLICATE transactionId documents block the unique index (the very
                // duplicates this change prevents going forward). Do NOT crash startup — the other guards
                // (idempotent seed, guarded probe promotion, callback-status delivery guard) still reduce
                // duplicates — but make the remediation loud.
                _logger.LogError(ex, "🧭 Could not create the unique index '{Index}' on fhirstatusevents.transactionId. " +
                    "If this is a duplicate-key error, de-duplicate existing documents (keep one per transactionId) and restart " +
                    "so transactionId uniqueness can be enforced.", TransactionIdUniqueIndexName);
            }
        }

        public async Task SeedPendingStatusEventAsync(StatusEvent ev, CancellationToken ct = default)
        {
            // Insert-if-absent keyed by transactionId. The unique index makes a concurrent duplicate insert
            // (e.g. the real SHIP callback landed first) fail; we treat that as "already seeded" and converge
            // on the existing document rather than creating a second one that would re-trigger EMR delivery.
            try
            {
                await StatusEventCol.InsertOneAsync(ev, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogInformation(
                    "📬 PENDING StatusEvent already exists for txn={Txn}; converging on it (no duplicate seeded).",
                    ev.TransactionId);
            }
        }

        // ── EMR callback delivery ──

        public async Task<List<StatusEvent>> FetchDueEmrCallbacksAsync(int batchSize, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");
            var now = DateTime.UtcNow;

            // Deliver EVERY terminal SHIP/MPI outcome (SUCCESS, ERROR, REJECTED, CONFLICT, DUPLICATE) — not
            // only successes — so the EMR always receives the final result. PENDING (awaiting-callback) is
            // excluded because it is not a terminal outcome.
            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Ne(x => x.CallbackStatus, "Succeeded"),
                Builders<StatusEvent>.Filter.Ne(x => x.CallbackStatus, "Failed"),
                Builders<StatusEvent>.Filter.In(x => x.Status, ShipCallbackStatus.Terminal),
                Builders<StatusEvent>.Filter.Lte(x => x.CallbackNextAttemptAt, now)
            );

            return await col.Find(filter)
                            .Sort(Builders<StatusEvent>.Sort.Ascending(x => x.CallbackNextAttemptAt))
                            .Limit(batchSize)
                            .ToListAsync(ct);
        }

        public async Task<bool> TryClaimEmrCallbackAsync(string id, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");
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

        public async Task MarkEmrCallbackSucceededAsync(string id, int statusCode, string? body, string? targetUrl, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");
            var update = Builders<StatusEvent>.Update
                .Set(x => x.CallbackStatus, "Succeeded")
                .Set(x => x.CallbackDeliveredAt, DateTime.UtcNow)
                .Set(x => x.EmrResponseStatusCode, statusCode)
                .Set(x => x.EmrResponseBody, Truncate(body, 4000))
                .Set(x => x.EmrTargetUrl, targetUrl);
            await col.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        public async Task MarkEmrCallbackRetryAsync(string id, string? error, TimeSpan delay, string? targetUrl, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");
            var update = Builders<StatusEvent>.Update
                .Inc(x => x.CallbackAttempts, 1)
                .Set(x => x.CallbackStatus, "Pending")
                .Set(x => x.CallbackLastError, Truncate(error, 2000))
                .Set(x => x.CallbackNextAttemptAt, DateTime.UtcNow.Add(delay))
                .Set(x => x.EmrTargetUrl, targetUrl);
            await col.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        /// <summary>Dead-letter a callback: mark Failed and stop polling (CallbackNextAttemptAt = null).</summary>
        public async Task MarkEmrCallbackFailedAsync(string id, string? error, string? targetUrl, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");
            var update = Builders<StatusEvent>.Update
                .Inc(x => x.CallbackAttempts, 1)
                .Set(x => x.CallbackStatus, "Failed")
                .Set(x => x.CallbackLastError, Truncate(error, 2000))
                .Set(x => x.CallbackNextAttemptAt, (DateTime?)null)
                .Set(x => x.EmrTargetUrl, targetUrl);
            await col.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        // ── Status probing ──

        public async Task<List<StatusEvent>> FetchDueStatusProbesAsync(TimeSpan age, int batchSize, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var olderThan = now - age;

            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Eq(x => x.Status, "PENDING"),
                Builders<StatusEvent>.Filter.Lte(x => x.ReceivedAtUtc, olderThan),
                Builders<StatusEvent>.Filter.Ne(x => x.ProbeStatus, "InFlight"),
                Builders<StatusEvent>.Filter.Ne(x => x.ProbeStatus, "Succeeded"),
                Builders<StatusEvent>.Filter.Lte(x => x.ProbeNextAttemptAt, now)
            );

            return await StatusEventCol.Find(filter)
                .Sort(Builders<StatusEvent>.Sort.Ascending(x => x.ProbeNextAttemptAt))
                .Limit(batchSize)
                .ToListAsync(ct);
        }

        public async Task<bool> TryClaimStatusProbeAsync(string id, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Eq(x => x.Id, id),
                Builders<StatusEvent>.Filter.Eq(x => x.Status, "PENDING"),
                Builders<StatusEvent>.Filter.Ne(x => x.ProbeStatus, "InFlight"),
                Builders<StatusEvent>.Filter.Ne(x => x.ProbeStatus, "Succeeded"),
                Builders<StatusEvent>.Filter.Lte(x => x.ProbeNextAttemptAt, now)
            );

            var update = Builders<StatusEvent>.Update
                .Set(x => x.ProbeStatus, "InFlight")
                .Set(x => x.ProbeLastError, null);

            var res = await StatusEventCol.UpdateOneAsync(filter, update, cancellationToken: ct);
            return res.ModifiedCount == 1;
        }

        public Task MarkProbeSucceededAsync(string id, CancellationToken ct = default)
        {
            var update = Builders<StatusEvent>.Update
                .Set(x => x.ProbeStatus, "Succeeded")
                .Set(x => x.ProbeNextAttemptAt, null);
            return StatusEventCol.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
        }

        public Task MarkProbeRetryAsync(string id, string? error, TimeSpan delay, bool abandon, CancellationToken ct = default)
        {
            var upd = Builders<StatusEvent>.Update
                .Set(x => x.ProbeStatus, abandon ? "Abandoned" : "Pending")
                .Set(x => x.ProbeLastError, Truncate(error, 2000))
                .Set(x => x.ProbeNextAttemptAt, DateTime.UtcNow.Add(delay))
                .Inc(x => x.ProbeAttempts, 1);

            return StatusEventCol.UpdateOneAsync(x => x.Id == id, upd, cancellationToken: ct);
        }

        public async Task<bool> MarkProbeSuccessAndAttachPayloadAsync(string id, string message, string? payloadJson, string? shipId, CancellationToken ct = default)
        {
            var col = _database.GetCollection<StatusEvent>("fhirstatusevents");

            BsonDocument? payload = null;
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try { payload = BsonDocument.Parse(payloadJson); }
                catch { payload = null; }
            }

            // Guard: only promote if the event is STILL pending. If the real SHIP callback already resolved it
            // to a terminal status, this matches nothing — the probe stands down rather than overwriting the
            // authoritative callback result (and delivery is not re-triggered).
            var filter = Builders<StatusEvent>.Filter.And(
                Builders<StatusEvent>.Filter.Eq(x => x.Id, id),
                Builders<StatusEvent>.Filter.Eq(x => x.Status, ShipCallbackStatus.Pending)
            );

            var update = Builders<StatusEvent>.Update
                .Set(x => x.Status, ShipCallbackStatus.Success)
                .Set(x => x.Message, message)
                .Set(x => x.Source, "PROBE")
                .Set(x => x.Data, payload)
                .Set(x => x.ProbeStatus, "Succeeded")
                .Set(x => x.ProbeLastError, null)
                .Set(x => x.ProbeNextAttemptAt, null);

            // Persist the SHIP identifier extracted from the probe response so the EMR callback carries the
            // same shipId a real SHIP callback would. Only set when present — never blank out an existing value.
            if (!string.IsNullOrWhiteSpace(shipId))
                update = update.Set(x => x.ShipId, shipId);

            var res = await col.UpdateOneAsync(filter, update, cancellationToken: ct);
            return res.ModifiedCount == 1;
        }

        private static string? Truncate(string? s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
