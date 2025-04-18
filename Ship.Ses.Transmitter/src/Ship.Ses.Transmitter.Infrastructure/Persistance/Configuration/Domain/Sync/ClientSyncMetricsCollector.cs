using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Encounter;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Ship.Ses.Transmitter.Worker
{
    public class ClientSyncMetricsCollector : ISyncMetricsCollector
    {
        private readonly IMongoDatabase _mongoDatabase;
        private readonly IClientSyncConfigProvider _configProvider;
        private readonly ILogger<ClientSyncMetricsCollector> _logger;
        private readonly Dictionary<string, Type> _resourceMap;

        public ClientSyncMetricsCollector(
            IMongoDatabase mongoDatabase,
            IClientSyncConfigProvider configProvider,
            ILogger<ClientSyncMetricsCollector> logger)
        {
            _mongoDatabase = mongoDatabase;
            _configProvider = configProvider;
            _logger = logger;

            _resourceMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                { "Patient", typeof(PatientSyncRecord) },
                { "Encounter", typeof(EncounterSyncRecord) }
                // Add new resource mappings here
            };
        }

        public async Task<SyncClientStatus> CollectStatusAsync(string clientId)
        {
            var (synced, failed) = await GetAggregateCountsAsync(clientId);

            return new SyncClientStatus
            {
                ClientId = clientId,
                Status = "Running",
                LastCheckIn = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow,
                TotalSynced = synced,
                TotalFailed = failed,
                CurrentBatchId = Guid.NewGuid().ToString(),
                LastError = null,
                IpAddress = GetLocalIpAddress(),
                Hostname = Dns.GetHostName(),
                Version = "1.0.0",
                SignatureHash = Guid.NewGuid().ToString(),
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async Task<IEnumerable<SyncClientMetric>> CollectMetricsAsync(string clientId)
        {
            var now = DateTime.UtcNow;
            var start = now.AddMinutes(-5);
            var enabledResources = await _configProvider.GetEnabledResourcesAsync(clientId);
            var result = new List<SyncClientMetric>();

            foreach (var resource in enabledResources)
            {
                if (!_resourceMap.TryGetValue(resource, out var modelType)) continue;

                var instance = (FhirSyncRecord)Activator.CreateInstance(modelType);
                var collection = _mongoDatabase.GetCollection<BsonDocument>(instance.CollectionName);

                var syncedFilter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("CreatedDate", start),
                    Builders<BsonDocument>.Filter.Lte("CreatedDate", now),
                    Builders<BsonDocument>.Filter.Eq("Status", "Synced"));

                var failedFilter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("CreatedDate", start),
                    Builders<BsonDocument>.Filter.Lte("CreatedDate", now),
                    Builders<BsonDocument>.Filter.Eq("Status", "Failed"));

                var syncedCount = (int)await collection.CountDocumentsAsync(syncedFilter);
                var failedCount = (int)await collection.CountDocumentsAsync(failedFilter);

                result.Add(new SyncClientMetric
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientId = clientId,
                    ResourceType = resource,
                    SyncWindowStart = start,
                    SyncWindowEnd = now,
                    SyncedCount = syncedCount,
                    FailedCount = failedCount,
                    BatchId = Guid.NewGuid().ToString(),
                    Notes = "Collected from MongoDB",
                    CreatedAt = now,
                    CreatedBy = clientId,
                    UpdatedAt = now,
                    UpdatedBy = clientId
                });
            }

            return result;
        }

        private async Task<(int synced, int failed)> GetAggregateCountsAsync(string clientId)
        {
            int synced = 0, failed = 0;
            var enabledResources = await _configProvider.GetEnabledResourcesAsync(clientId);

            foreach (var resource in enabledResources)
            {
                if (!_resourceMap.TryGetValue(resource, out var modelType)) continue;

                var instance = (FhirSyncRecord)Activator.CreateInstance(modelType);
                var collection = _mongoDatabase.GetCollection<BsonDocument>(instance.CollectionName);

                var syncedFilter = Builders<BsonDocument>.Filter.Eq("Status", "Synced");
                var failedFilter = Builders<BsonDocument>.Filter.Eq("Status", "Failed");

                synced += (int)await collection.CountDocumentsAsync(syncedFilter);
                failed += (int)await collection.CountDocumentsAsync(failedFilter);
            }

            return (synced, failed);
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "Unknown";
        }
    }


}