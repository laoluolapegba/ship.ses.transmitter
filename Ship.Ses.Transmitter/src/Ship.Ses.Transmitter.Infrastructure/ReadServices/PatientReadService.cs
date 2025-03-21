using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices 
{
    public class PatientReadService : IPatientReadService
    {
        private readonly IPatientRepository _patientRepository;

        public PatientReadService(IPatientRepository patientRepository)
        {
            _patientRepository = patientRepository;
        }
        
        public async Task<IEnumerable<Patient>> GetPendingPatientsAsync()
        {
            return await _patientRepository.GetPendingPatientsAsync();
        }
    }
}
