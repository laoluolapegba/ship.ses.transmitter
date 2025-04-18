using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    public interface IFhirSyncService
    {
        //Task ProcessPendingRecordsAsync(FhirResourceType resourceType, CancellationToken cancellationToken);
        //Task ProcessPendingRecordsAsync<T>(CancellationToken token) where T : FhirSyncRecord, new();
        Task<SyncResultDto> ProcessPendingRecordsAsync<T>(CancellationToken token) 
    where T : FhirSyncRecord, new();
    }
}
