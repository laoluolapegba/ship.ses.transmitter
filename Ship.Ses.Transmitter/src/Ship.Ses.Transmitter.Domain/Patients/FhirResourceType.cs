using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public enum FhirResourceType
    {
        Patient,
        Encounter,
        Observation,
        MedicationRequest,
        Condition
    }
}
