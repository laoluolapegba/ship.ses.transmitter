using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.DTOs
{
    /// <summary>
    /// Represents the overall health status of the Smart Health Information Platform (SHIP) Transmitter API.
    /// </summary>
    public class ApiHealthStatusDto
    {
        /// <summary>
        /// The overall status of the API (e.g., "Healthy", "Degraded", "Unhealthy").
        /// </summary>
        public string OverallStatus { get; set; } = string.Empty;

        /// <summary>
        /// The current timestamp when this health check was performed (UTC).
        /// </summary>
        public DateTime CheckedAtUtc { get; set; }

        /// <summary>
        /// The version of the API currently running.
        /// </summary>
        public string ApiVersion { get; set; } = string.Empty;

        /// <summary>
        /// Optional: A general message providing more context about the health status.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// A collection of health checks for individual components or dependencies.
        /// </summary>
        public List<ComponentHealthStatusDto> Components { get; set; } = new List<ComponentHealthStatusDto>();

        /// <summary>
        /// Optional: Total elapsed time for the health check operation in milliseconds.
        /// </summary>
        public long TotalDurationMilliseconds { get; set; }
    }
    /// <summary>
    /// Represents the health status of a specific component or dependency.
    /// </summary>
    public class ComponentHealthStatusDto
    {
        /// <summary>
        /// The name of the component or dependency (e.g., "Database", "Message Broker", "External Service X").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The status of this specific component (e.g., "Healthy", "Unhealthy", "Degraded").
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Optional: A description or error message if the component is unhealthy or degraded.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional: The duration of this specific component's health check in milliseconds.
        /// </summary>
        public long DurationMilliseconds { get; set; }
    }
}
