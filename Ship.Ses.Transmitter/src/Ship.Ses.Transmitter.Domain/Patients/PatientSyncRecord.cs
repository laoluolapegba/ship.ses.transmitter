

namespace Ship.Ses.Transmitter.Domain.Patients
{
    

    [FhirResource("Patient")] // lets the worker map this class to the "Patient" resource explicitly
    public sealed class PatientSyncRecord : FhirSyncRecord
    {
        public override string CollectionName => "transformed_pool_patients";

        public PatientSyncRecord()
        {
            ResourceType = "Patient";
        }
    }
}