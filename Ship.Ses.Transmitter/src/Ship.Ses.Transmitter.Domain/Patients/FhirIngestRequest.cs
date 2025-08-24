using System.Text.Json.Nodes;
using Swashbuckle.AspNetCore.Annotations;
namespace Ship.Ses.Transmitter.Domain.Patients
{
    /// <summary>
    /// Request payload for submitting a FHIR resource
    /// </summary>
    public class FhirIngestRequest
    {
        /// <summary>
        /// The FHIR resource type (e.g., Patient, Encounter)
        /// </summary>
        [SwaggerSchema("The type of FHIR resource to ingest.", Nullable = false)]
        public string ResourceType { get; set; }

        /// <summary>
        /// The client-assigned resource ID (optional)
        /// </summary>
        [SwaggerSchema("Optional EMR-assigned resource identifier.")]
        public string ResourceId { get; set; }

        /// <summary>
        /// The actual FHIR-compliant JSON object
        /// </summary>
        [SwaggerSchema("FHIR-compliant resource body as JSON.")]
        public JsonObject FhirJson { get; set; }

        /// <summary>
        /// Optional metadata or source system indicator
        /// </summary>
        [SwaggerSchema("The client id / SHIP faciltiy ID of the source EMR.")]
        public required string FacilityId { get; set; }
    }
}
