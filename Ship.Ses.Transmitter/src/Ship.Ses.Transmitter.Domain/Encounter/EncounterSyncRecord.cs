using Ship.Ses.Transmitter.Domain.Patients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Encounter
{
    public class EncounterSyncRecord : FhirSyncRecord
    {
        public override string CollectionName => "transformed_pool_resources";
    }

}
