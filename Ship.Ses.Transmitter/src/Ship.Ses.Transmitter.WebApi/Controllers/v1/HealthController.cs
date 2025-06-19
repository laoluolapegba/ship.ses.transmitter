using Microsoft.AspNetCore.Mvc;
using Ship.Ses.Transmitter.Application.DTOs;
using System.Diagnostics;
using System.Reflection;

namespace Ship.Ses.Transmitter.WebApi.Controllers.v1
{
    [ApiController]
    [Route("api/health")] // Often /health or /status
    [ApiVersionNeutral] // Important: Health endpoints are usually version-neutral
    public class HealthController : ControllerBase
    {
        // You might inject services here that perform actual health checks
        // private readonly IDatabaseHealthCheckService _dbHealthCheck;
        // private readonly IMessageBrokerHealthCheckService _mbHealthCheck;

        public HealthController(/* injected services */)
        {
            // _dbHealthCheck = dbHealthCheck;
            // _mbHealthCheck = mbHealthCheck;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiHealthStatusDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)] // If overall status is unhealthy
        public async Task<IActionResult> GetHealthStatus()
        {
            var stopwatch = Stopwatch.StartNew();
            var healthStatus = new ApiHealthStatusDto
            {
                CheckedAtUtc = DateTime.UtcNow,
                ApiVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
                OverallStatus = "Healthy" // Assume healthy unless a component indicates otherwise
            };

            // --- Perform actual health checks for components ---
            // Example: Database Health Check
            var dbComponent = new ComponentHealthStatusDto { Name = "Database" };
            var dbStopwatch = Stopwatch.StartNew();
            try
            {
                // Call your actual database health check logic here
                // bool isDbHealthy = await _dbHealthCheck.CheckAsync();
                bool isDbHealthy = true; // Placeholder for demo
                if (isDbHealthy)
                {
                    dbComponent.Status = "Healthy";
                    dbComponent.Description = "Database connection successful.";
                }
                else
                {
                    dbComponent.Status = "Unhealthy";
                    dbComponent.Description = "Failed to connect to the database.";
                    healthStatus.OverallStatus = "Degraded"; // Or "Unhealthy" if critical
                }
            }
            catch (Exception ex)
            {
                dbComponent.Status = "Unhealthy";
                dbComponent.Description = $"Database check failed: {ex.Message}";
                healthStatus.OverallStatus = "Unhealthy";
            }
            finally
            {
                dbStopwatch.Stop();
                dbComponent.DurationMilliseconds = dbStopwatch.ElapsedMilliseconds;
                healthStatus.Components.Add(dbComponent);
            }

            // Example: Message Broker Health Check
            var mbComponent = new ComponentHealthStatusDto { Name = "Message Broker" };
            var mbStopwatch = Stopwatch.StartNew();
            try
            {
                // Call your actual message broker health check logic here
                // bool isMbHealthy = await _mbHealthCheck.CheckAsync();
                bool isMbHealthy = true; // Placeholder for demo
                if (isMbHealthy)
                {
                    mbComponent.Status = "Healthy";
                    mbComponent.Description = "Message broker connected.";
                }
                else
                {
                    mbComponent.Status = "Degraded";
                    mbComponent.Description = "Could not send test message to broker.";
                    if (healthStatus.OverallStatus != "Unhealthy") // Don't downgrade from Unhealthy
                        healthStatus.OverallStatus = "Degraded";
                }
            }
            catch (Exception ex)
            {
                mbComponent.Status = "Unhealthy";
                mbComponent.Description = $"Message broker check failed: {ex.Message}";
                healthStatus.OverallStatus = "Unhealthy";
            }
            finally
            {
                mbStopwatch.Stop();
                mbComponent.DurationMilliseconds = mbStopwatch.ElapsedMilliseconds;
                healthStatus.Components.Add(mbComponent);
            }
            // --- End of health checks ---

            stopwatch.Stop();
            healthStatus.TotalDurationMilliseconds = stopwatch.ElapsedMilliseconds;

            if (healthStatus.OverallStatus == "Unhealthy")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, healthStatus);
            }
            else
            {
                return Ok(healthStatus);
            }
        }
    }
}
