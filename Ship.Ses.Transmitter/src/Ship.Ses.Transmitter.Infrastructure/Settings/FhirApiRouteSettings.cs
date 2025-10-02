using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Infrastructure.Settings;

public class FhirApiRouteSettings
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public FhirApiClientCertSettings? ClientCert { get; set; }
    public string? Scope { get; set; }
    public IReadOnlyCollection<string>? Resources { get; set; }
}
