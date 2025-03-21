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

namespace Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Patients
{
    public class PatientRepository : IPatientRepository
    {
        private readonly IMongoCollection<Patient> _patients;

        public PatientRepository(IOptions<SourceDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var database = client.GetDatabase(settings.Value.DatabaseName);
            _patients = database.GetCollection<Patient>("patients");
        }

        public async Task<IEnumerable<Patient>> GetPendingPatientsAsync()
        {
            return await _patients.Find(p => p.SyncStatus == "Pending").ToListAsync();
        }

        public async Task UpdateSyncStatusAsync(string patientId, string status, string message)
        {
            var update = Builders<Patient>.Update
                .Set(p => p.SyncStatus, status)
                .Set(p => p.SyncMessage, message)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            await _patients.UpdateOneAsync(p => p.PatientId == patientId, update);
        }
        
    }
}
