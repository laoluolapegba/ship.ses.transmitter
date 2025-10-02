using System;
using System.Collections.Generic;
using System.Linq;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public sealed class FhirRoutingSettings
    {
        public FhirRouteSettings Default { get; set; } = new();
        public List<FhirApiRouteSettings> Apis { get; set; } = new();

        public (string RouteName, FhirRouteSettings Settings) ResolveRoute(string? shipService, string resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
                throw new ArgumentException("Resource type is required", nameof(resourceType));

            if (!string.IsNullOrWhiteSpace(shipService))
            {
                var matchByName = Apis.FirstOrDefault(a =>
                    string.Equals(a.Name, shipService, StringComparison.OrdinalIgnoreCase));
                if (matchByName is not null)
                    return (matchByName.Name, matchByName);
            }

            var matchByResource = Apis.FirstOrDefault(a => a.Resources.Any(r =>
                string.Equals(r, resourceType, StringComparison.OrdinalIgnoreCase)));
            if (matchByResource is not null)
                return (matchByResource.Name, matchByResource);

            return ("Default", Default ?? throw new InvalidOperationException("FhirRouting:Default is not configured"));
        }
    }

    public class FhirRouteSettings
    {
        public string BaseUrl { get; set; } = default!;
        public int TimeoutSeconds { get; set; } = 30;
        public FhirClientCertificateSettings? ClientCert { get; set; }
        public string? CallbackUrlTemplate { get; set; }
        public string? Scope { get; set; }
    }

    public sealed class FhirApiRouteSettings : FhirRouteSettings
    {
        public string Name { get; set; } = default!;
        public List<string> Resources { get; set; } = new();
    }

    public sealed class FhirClientCertificateSettings
    {
        public string Path { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
