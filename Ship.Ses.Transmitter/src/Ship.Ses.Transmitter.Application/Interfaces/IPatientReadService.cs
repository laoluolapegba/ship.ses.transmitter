
using Ship.Ses.Transmitter.Domain.Patients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    public interface IPatientReadService
    {
        Task<IEnumerable<Patient>> GetPendingPatientsAsync();
    }
}
