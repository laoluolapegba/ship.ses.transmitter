using Ship.Ses.Transmitter.Application.Authentication.LoginUser;
using Ship.Ses.Transmitter.Application.Authentication.RefreshUserToken;
using Ship.Ses.Transmitter.Application.Authentication.ReLoginCustomer;
using MassTransit.Mediator;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Ship.Ses.Transmitter.Application.Patients;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.WebApi.Filters;
using System.Net;
namespace Ship.Ses.Transmitter.WebApi.Controllers.v1
{

    /// <summary>
    /// Allows EMRs to submit FHIR-compliant resources into the SHIP SES platform.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/fhir-ingest")]
    [SwaggerTag("Endpoints for ingesting FHIR-compliant resources from external EMRs.")]
    public class FhirIngestController : ControllerBase
    {
        private readonly IFhirIngestService _ingestService;
        private readonly ILogger<FhirIngestController> _logger;

        public FhirIngestController(IFhirIngestService ingestService, ILogger<FhirIngestController> logger)
        {
            _ingestService = ingestService;
            _logger = logger;
        }

        

        /// <summary>
        /// Accepts a FHIR-compliant resource payload and stores it for processing.
        /// </summary>
        /// <param name="request">FHIR resource wrapper with metadata</param>
        /// <returns>A success or error response</returns>
        [HttpPost]
        [SwaggerOperation(
            Summary = "Submit a FHIR resource",
            Description = "Allows external EMRs to push FHIR-compliant resource data (e.g., Patient, Encounter) into the SHIP SES ingest queue.",
            OperationId = "FhirIngest_SubmitResource",
            Tags = new[] { "FHIR Ingest" }
        )]

        //[SwaggerRequestExample(typeof(FhirIngestRequest), typeof(FhirIngestRequestExample))]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        [ProducesResponseType(typeof(string), 500)]
        public async Task<IActionResult> Post([FromBody] FhirIngestRequest request)
        {
            if (request == null)
            {
                _logger.LogWarning("❌ Request body is null.");
                return BadRequest("Request cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.ResourceType))
            {
                _logger.LogWarning("❌ Missing required field: ResourceType");
                return BadRequest("Missing required field: ResourceType.");
            }

            try
            {
                _logger.LogInformation("📥 Ingesting FHIR resource of type {ResourceType} from EMR source.", request.ResourceType);
                await _ingestService.IngestAsync(request);

                return Accepted(new
                {
                    status = "accepted",
                    resourceType = request.ResourceType,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔥 Unexpected error during FHIR ingest.");
                return StatusCode(500, "Internal server error.");
            }
        }
    }
}
