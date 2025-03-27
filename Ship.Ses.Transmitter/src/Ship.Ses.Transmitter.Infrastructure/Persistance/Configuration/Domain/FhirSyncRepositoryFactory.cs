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
    public class FhirSyncRepositoryFactory : IFhirSyncRepositoryFactory
    {
        private readonly IMongoDatabase _database;

        public FhirSyncRepositoryFactory(IMongoClient mongoClient, IOptions<SourceDbSettings> options)
        {
            _database = mongoClient.GetDatabase(options.Value.DatabaseName);
        }

        public IFhirSyncRepository Create(string collectionName)
        {
            return new FhirSyncRepository(_database, collectionName);
        }
    }
}
