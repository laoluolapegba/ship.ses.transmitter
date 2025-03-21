using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public interface IFhirSyncRepository
    {
        Task<IEnumerable<FhirSyncRecord>> GetPendingRecordsAsync(FhirResourceType resourceType);
        Task AddRecordAsync(FhirSyncRecord record);
        Task UpdateRecordAsync(FhirSyncRecord record);
    }
}
