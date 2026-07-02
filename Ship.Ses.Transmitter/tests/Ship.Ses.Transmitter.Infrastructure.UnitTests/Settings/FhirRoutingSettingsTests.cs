using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Settings;

/// <summary>
/// Pins the design rule that shipService/targetSystem drives ROUTING ONLY.
/// (See docs/multi-client/FINDINGS.md §4.1.)
/// </summary>
public class FhirRoutingSettingsTests
{
    private static FhirRoutingSettings BuildRouting() => new()
    {
        Default = new FhirRouteSettings { BaseUrl = "https://gateway/fhir" },
        Apis = new()
        {
            new FhirApiRouteSettings
            {
                Name = "PDS",
                BaseUrl = "https://pds",
                Resources = { "Patient" }
            },
            new FhirApiRouteSettings
            {
                Name = "SCR",
                BaseUrl = "https://scr",
                Resources = { "Observation", "Condition" }
            }
        }
    };

    [Fact]
    public void ResolveRoute_ByServiceName_TakesPrecedence()
    {
        // shipService "SCR" wins even though the resource (Patient) belongs to PDS.
        var (name, route) = BuildRouting().ResolveRoute("SCR", "Patient");

        Assert.Equal("SCR", name);
        Assert.Equal("https://scr", route.BaseUrl);
    }

    [Fact]
    public void ResolveRoute_ByServiceName_IsCaseInsensitive()
    {
        var (name, _) = BuildRouting().ResolveRoute("pds", "Patient");

        Assert.Equal("PDS", name);
    }

    [Fact]
    public void ResolveRoute_NoService_FallsBackToResourceMatch()
    {
        var (name, route) = BuildRouting().ResolveRoute(shipService: null, "Observation");

        Assert.Equal("SCR", name);
        Assert.Equal("https://scr", route.BaseUrl);
    }

    [Fact]
    public void ResolveRoute_UnknownService_AndUnknownResource_FallsBackToDefault()
    {
        var (name, route) = BuildRouting().ResolveRoute("Nope", "Unknown");

        Assert.Equal("Default", name);
        Assert.Equal("https://gateway/fhir", route.BaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveRoute_BlankResourceType_Throws(string? resourceType)
    {
        Assert.Throws<ArgumentException>(() => BuildRouting().ResolveRoute("PDS", resourceType!));
    }
}
