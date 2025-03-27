using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Domain;
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
        private readonly Dictionary<string, ResourceSyncSettings> _resourceConfig;

        public ClientSyncMetricsCollector(IMongoDatabase mongoDatabase, 
            IOptions<SyncOptions> options)
        {
            if (mongoDatabase == null)
                throw new ArgumentNullException(nameof(mongoDatabase), "❌ IMongoDatabase is null. Check DI setup.");

            if (options == null || options.Value == null)
                throw new ArgumentNullException(nameof(options), "❌ SyncOptions is null or missing in configuration.");


            _mongoDatabase = mongoDatabase;
            _resourceConfig = new Dictionary<string, ResourceSyncSettings>
            {
                { "Patient", options.Value.Patient },
                { "Encounter", options.Value.Encounter }
                // Add more resources here as needed in config and class
            };
            if (_resourceConfig.Values.All(v => v == null))
            {
                throw new InvalidOperationException("❌ No resource config was loaded. Check appsettings.json ResourceSync section.");
            }
            foreach (var kv in _resourceConfig)
            {
                Console.WriteLine($"Resource: {kv.Key}, Collection: {kv.Value?.CollectionName}, Enabled: {kv.Value?.Enabled}");
            }
        }

        public SyncClientStatus CollectStatus(string clientId)
        {
            var (synced, failed) = GetAggregateCounts();

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

        public IEnumerable<SyncClientMetric> CollectMetrics(string clientId)
        {
            var now = DateTime.UtcNow;
            var start = now.AddMinutes(-5);

            return _resourceConfig
                .Where(kv => kv.Value.Enabled)
                .Select(kv =>
                {
                    var resource = kv.Key;
                    var collectionName = kv.Value.CollectionName;
                    var collection = _mongoDatabase.GetCollection<MongoDocumentWrapper>(collectionName);

                    var syncedFilter = Builders<MongoDocumentWrapper>.Filter.And(
                        Builders<MongoDocumentWrapper>.Filter.Gte(x => x.CreatedDate, start),
                        Builders<MongoDocumentWrapper>.Filter.Lte(x => x.CreatedDate, now),
                        Builders<MongoDocumentWrapper>.Filter.Eq(x => x.Status, "Synced")
                    );

                    var failedFilter = Builders<MongoDocumentWrapper>.Filter.And(
                        Builders<MongoDocumentWrapper>.Filter.Gte(x => x.CreatedDate, start),
                        Builders<MongoDocumentWrapper>.Filter.Lte(x => x.CreatedDate, now),
                        Builders<MongoDocumentWrapper>.Filter.Eq(x => x.Status, "Failed")
                    );

                    var syncedCount = (int)collection.CountDocuments(syncedFilter);
                    var failedCount = (int)collection.CountDocuments(failedFilter);

                    return new SyncClientMetric
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
                    };
                }).ToList();
        }

        private (int synced, int failed) GetAggregateCounts()
        {
            int synced = 0, failed = 0;

            foreach (var kv in _resourceConfig.Where(kv => kv.Value.Enabled))
            {
                var collection = _mongoDatabase.GetCollection<MongoDocumentWrapper>(kv.Value.CollectionName);

                var syncedFilter = Builders<MongoDocumentWrapper>.Filter.Eq(x => x.Status, "Synced");
                var failedFilter = Builders<MongoDocumentWrapper>.Filter.Eq(x => x.Status, "Failed");

                synced += (int)collection.CountDocuments(syncedFilter);
                failed += (int)collection.CountDocuments(failedFilter);
            }

            return (synced, failed);
        }
        private string GetLocalIpAddress()
        {
            string ip = "Unknown";
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ipAddress in host.AddressList)
                {
                    if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ip = ipAddress.ToString();
                        break;
                    }
                }
            }
            catch { }
            return ip;
        }

        private class MongoDocumentWrapper
        {
            public DateTime CreatedDate { get; set; }
            public string Status { get; set; }
        }
    }


}