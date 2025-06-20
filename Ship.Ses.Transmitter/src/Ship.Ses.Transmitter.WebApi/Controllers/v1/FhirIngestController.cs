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
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Ship.Ses.Transmitter.Application.Interfaces;
namespace Ship.Ses.Transmitter.WebApi.Controllers.v1
{

    /// <summary>
    /// Allows EMRs to submit FHIR-compliant resources into the SHIP SES platform.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/fhir-ingest")]
    [SwaggerTag("Endpoints for ingesting FHIR-compliant resources from external EMRs.")]
    [Authorize]
    public class FhirIngestController : ControllerBase
    {
        private readonly IFhirIngestService _ingestService;
        private readonly ILogger<FhirIngestController> _logger;
        private readonly IClientSyncConfigProvider _clientConfig;

        public FhirIngestController(IFhirIngestService ingestService, ILogger<FhirIngestController> logger, IClientSyncConfigProvider clientConfig)
        {
            _ingestService = ingestService;
            _logger = logger;
            _clientConfig = clientConfig;
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
            var clientId = User.FindFirst("client_id")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogWarning("❌ Could not extract client_id from JWT claims");
                return Unauthorized(new { message = "Missing or invalid authentication context" });
            }

            _logger.LogInformation("🔐 Authenticated request from client: {ClientId}", clientId);
            if (!await _clientConfig.IsClientActiveAsync(clientId))
            {
                _logger.LogWarning("❌ Unknown or inactive client attempted ingestion: {ClientId}", clientId);
                return Unauthorized(new { message = $"Client '{clientId}' is not registered or not active" });
            }
            if (string.IsNullOrWhiteSpace(request.ResourceType))
            {
                _logger.LogWarning("❌ Missing required field: ResourceType");
                return BadRequest("Missing required field: ResourceType.");
            }

            try
            {
                _logger.LogInformation("📥 Ingesting FHIR resource of type {ResourceType} from EMR source.", request.ResourceType);
                await _ingestService.IngestAsync(request, clientId);

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
