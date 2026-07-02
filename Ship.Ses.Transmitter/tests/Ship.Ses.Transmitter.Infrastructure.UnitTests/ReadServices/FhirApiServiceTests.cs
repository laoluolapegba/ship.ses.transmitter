using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Services;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Infrastructure.UnitTests.Fakes;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.ReadServices;

/// <summary>
/// Pins the outbound FHIR call: per-client credential resolution (bearer from <see cref="IFhirTokenService"/>
/// keyed by the record's clientId), route-derived scope, route/path selection, payload enveloping, and
/// HTTP error → <see cref="Ship.Ses.Transmitter.Domain.SyncModels.FhirApiResponse"/> mapping.
/// (FINDINGS.md §2.5, §4.1, §4.2, §4.4.)
/// </summary>
public class FhirApiServiceTests
{
    private static FhirRoutingSettings Routing(string defaultBaseUrl = "https://gateway/fhir") => new()
    {
        Default = new FhirRouteSettings { BaseUrl = defaultBaseUrl, CallbackUrlTemplate = "https://cb/default" },
        Apis =
        {
            new FhirApiRouteSettings { Name = "PDS", BaseUrl = "https://pds", Resources = { "Patient" } }
        }
    };

    private sealed class Harness
    {
        public FhirApiService Sut { get; set; } = null!;
        public HttpRequestMessage? LastRequest { get; set; }
        public string? LastBody { get; set; }
        public string? CapturedClientId { get; set; }
        public string? CapturedScope { get; set; }
    }

    private static Harness CreateSut(
        FhirRoutingSettings? routing = null,
        AuthSettings? auth = null,
        int responseStatus = 202,
        string responseBody = """{"status":"success","code":202,"message":"accepted","transactionId":"txn-1"}""")
    {
        routing ??= Routing();
        auth ??= new AuthSettings { TokenEndpoint = "https://identity/token", ClientId = "cfg", ClientSecret = "s", Scope = "auth-scope" };

        var harness = new Harness();

        var handler = new CountingHttpMessageHandler(req =>
        {
            harness.LastRequest = req;
            harness.LastBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage((HttpStatusCode)responseStatus)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(() => new HttpClient(handler, disposeHandler: false));

        var monitor = new Mock<IOptionsMonitor<FhirRoutingSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(routing);

        var token = new Mock<IFhirTokenService>();
        token.Setup(t => t.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Callback<string, string?, CancellationToken>((c, s, _) => { harness.CapturedClientId = c; harness.CapturedScope = s; })
             .ReturnsAsync("test-token");

        harness.Sut = new FhirApiService(factory.Object, monitor.Object, Options.Create(auth), NullLogger<FhirApiService>.Instance, token.Object);
        return harness;
    }

    [Fact]
    public async Task Post_DefaultRoute_PostsToBaseUrl_AndWrapsPayloadInEnvelope()
    {
        var h = CreateSut();

        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Observation",
            jsonPayload: """{"resourceType":"Observation","id":"o1"}""");

        Assert.Equal(HttpMethod.Post, h.LastRequest!.Method);
        Assert.Equal("https://gateway/fhir", h.LastRequest!.RequestUri!.ToString());

        using var body = JsonDocument.Parse(h.LastBody!);
        Assert.True(body.RootElement.TryGetProperty("callbackUrl", out _));    // enveloped
        Assert.True(body.RootElement.TryGetProperty("data", out var data));
        Assert.Equal("Observation", data.GetProperty("resourceType").GetString());
    }

    [Fact]
    public async Task SetsBearerAuthorizationHeader_FromTokenService()
    {
        var h = CreateSut();

        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Observation",
            jsonPayload: """{"resourceType":"Observation"}""");

        Assert.Equal("Bearer", h.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", h.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ResolvesToken_ForRecordClientId_AndAuthSettingsScope()
    {
        var h = CreateSut();

        // Credential keyed by the record's own clientId; scope comes from AuthSettings, not the route.
        await h.Sut.SendAsync(FhirOperation.Post, "client-x", "Patient",
            jsonPayload: """{"resourceType":"Patient"}""", shipService: "PDS");

        Assert.Equal("client-x", h.CapturedClientId);
        Assert.Equal("auth-scope", h.CapturedScope);
    }

    [Fact]
    public async Task Scope_ComesFromAuthSettings_RegardlessOfRoute()
    {
        // Authorization is never shipService-specific: the AuthSettings scope is used whether the
        // request routes to a named API (PDS) or the Default route.
        var h = CreateSut();

        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Observation",
            jsonPayload: """{"resourceType":"Observation"}""");          // → Default route
        var defaultScope = h.CapturedScope;

        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Patient",
            jsonPayload: """{"resourceType":"Patient"}""", shipService: "PDS");  // → PDS route

        Assert.Equal("auth-scope", defaultScope);
        Assert.Equal("auth-scope", h.CapturedScope);
    }

    [Fact]
    public async Task Pds_PatientPost_BuildsApiV1ResourcePath()
    {
        var h = CreateSut();

        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Patient",
            jsonPayload: """{"resourceType":"Patient"}""", shipService: "PDS");

        Assert.Equal("https://pds/api/v1/Patient", h.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Pds_BundlePayload_RoutesToBundleEndpoint()
    {
        var h = CreateSut();

        // Payload resourceType drives bundle detection regardless of the declared resourceType.
        await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Patient",
            jsonPayload: """{"resourceType":"Bundle","type":"transaction"}""", shipService: "PDS");

        Assert.Equal("https://pds/api/v1/Bundle", h.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SuccessJson_IsParsedIntoResponse()
    {
        var h = CreateSut();

        var result = await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Patient",
            jsonPayload: """{"resourceType":"Patient"}""", shipService: "PDS");

        Assert.Equal(202, result.Code);
        Assert.Equal("success", result.Status);
        Assert.Equal("txn-1", result.transactionId);
    }

    [Fact]
    public async Task NonSuccessStatus_ReturnsErrorResponse_WithCodeAndMessage()
    {
        var h = CreateSut(responseStatus: 400,
            responseBody: """{"status":"error","message":"bad request"}""");

        var result = await h.Sut.SendAsync(FhirOperation.Post, "client-a", "Patient",
            jsonPayload: """{"resourceType":"Patient"}""", shipService: "PDS");

        Assert.Equal(400, result.Code);
        Assert.Equal("error", result.Status);
        Assert.Equal("bad request", result.Message);
    }

    [Fact]
    public async Task MissingBaseUrl_Throws()
    {
        var routing = Routing(defaultBaseUrl: "");
        var h = CreateSut(routing: routing);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Sut.SendAsync(FhirOperation.Post, "client-a", "Observation",
                jsonPayload: """{"resourceType":"Observation"}"""));
    }
}
