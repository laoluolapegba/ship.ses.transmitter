using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Worker
{
    public class ResourceSyncMap
    {
        public string? ResourceName { get; set; }
        public Type RecordType { get; set; } // e.g. typeof(PatientSyncRecord)
    }
}
