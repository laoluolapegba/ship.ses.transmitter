using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Patients
{
    public class FhirSyncRecord
    {
        public string Id { get; set; }
        public FhirResourceType ResourceType { get; set; }
        public string ResourceId { get; set; }
        public string FhirJson { get; set; }
        public string Status { get; set; } // Pending, Synced, Failed
        public DateTime CreatedDate { get; set; }
        public DateTime? TimeSynced { get; set; }
        public int RetryCount { get; set; }
        public string ErrorMessage { get; set; }
        public string SyncedResourceId { get; set; } // Updated after sync
    }
}
