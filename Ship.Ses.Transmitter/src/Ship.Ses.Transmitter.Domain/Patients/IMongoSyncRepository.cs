using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public interface IMongoSyncRepository
    {
        //Task<IEnumerable<FhirSyncRecord>> GetPendingRecordsAsync(FhirResourceType resourceType);
        Task<IEnumerable<T>> GetPendingRecordsAsync<T>() where T : FhirSyncRecord, new();
        Task AddRecordAsync<T>(T record) where T : FhirSyncRecord;
        Task UpdateRecordAsync<T>(T record) where T : FhirSyncRecord;
        Task<IEnumerable<T>> GetByStatusAsync<T>(string status, int skip = 0, int take = 100) where T : FhirSyncRecord, new();
        
        Task BulkUpdateStatusAsync<T>(
    Dictionary<ObjectId, (string status, string message, string transactionId, string rawResponse)> updates
) where T : FhirSyncRecord, new();
    }
}
