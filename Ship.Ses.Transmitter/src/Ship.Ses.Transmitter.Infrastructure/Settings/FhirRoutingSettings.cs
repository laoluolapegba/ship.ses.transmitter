using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Infrastructure.Settings;

public class FhirRoutingSettings
{
    public string? CallbackUrlTemplate { get; set; }
    public FhirApiRouteSettings? Default { get; set; }
    public IReadOnlyCollection<FhirApiRouteSettings>? Apis { get; set; }
}
