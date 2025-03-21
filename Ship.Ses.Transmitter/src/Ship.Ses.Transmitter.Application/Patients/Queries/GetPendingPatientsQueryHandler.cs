using Ship.Ses.Transmitter.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ship.Ses.Transmitter.Domain.Patients;

namespace Ship.Ses.Transmitter.Application.Patients.Queries
{
    public class GetPendingPatientsQueryHandler
    {
        private readonly IPatientReadService _patientReadService;

        public GetPendingPatientsQueryHandler(IPatientReadService patientReadService)
        {
            _patientReadService = patientReadService;
        }

        public async Task<IEnumerable<Patient>> Handle(GetPendingPatientsQuery query)
        {
            return await _patientReadService.GetPendingPatientsAsync();
        }
    }
}
