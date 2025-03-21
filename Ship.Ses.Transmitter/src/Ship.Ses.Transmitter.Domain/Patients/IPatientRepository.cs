using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public interface IPatientRepository
    {
        Task<IEnumerable<Patient>> GetPendingPatientsAsync();
        Task UpdateSyncStatusAsync(string patientId, string status, string message);
    }
}
