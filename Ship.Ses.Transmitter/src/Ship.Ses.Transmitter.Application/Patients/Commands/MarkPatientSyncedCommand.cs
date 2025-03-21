using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Patients.Commands
{
    public class MarkPatientSyncedCommand
    {
        public string PatientId { get; }
        public string Status { get; }
        public string Message { get; }

        public MarkPatientSyncedCommand(string patientId, string status, string message)
        {
            PatientId = patientId;
            Status = status;
            Message = message;
        }
    }
}
