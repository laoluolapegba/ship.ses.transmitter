using Ship.Ses.Transmitter.Domain.Patients;

namespace Ship.Ses.Transmitter.Domain.Queue
{
    [FhirResource("Observation")]
    [FhirResource("AllergyIntolerance")]
    [FhirResource("Condition")]
    public sealed class QueueSyncRecord : FhirSyncRecord
    {
        public override string CollectionName => "transformed_pool_queue";

        public QueueSyncRecord()
        {
            ResourceType = string.Empty;
        }
    }
}
