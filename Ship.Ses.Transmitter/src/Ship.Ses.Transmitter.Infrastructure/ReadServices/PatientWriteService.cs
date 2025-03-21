using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public class PatientWriteService : IPatientWriteService
    {
        private readonly IPatientRepository _patientRepository;

        public PatientWriteService(IPatientRepository patientRepository)
        {
            _patientRepository = patientRepository;
        }

        public async Task MarkPatientAsSyncedAsync(string patientId, string status, string message)
        {
            await _patientRepository.UpdateSyncStatusAsync(patientId, status, message);
        }
    }
}
