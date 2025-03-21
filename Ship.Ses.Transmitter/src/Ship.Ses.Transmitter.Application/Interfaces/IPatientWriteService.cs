using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    public interface IPatientWriteService
    {
        Task MarkPatientAsSyncedAsync(string patientId, string status, string message);
    }
}
