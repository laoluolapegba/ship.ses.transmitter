using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.UnitTests.Fakes;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

/// <summary>
/// Pins the per-(clientId, scope) token cache: keying, expiry/refresh-ahead, and single-flight.
/// (FINDINGS.md §2.1, §2.2; Build Plan Phase 2.)
/// </summary>
public class CachedFhirTokenServiceTests
{
    private static readonly ClientCredential Cred = new("https://identity/token", "lakeshore", "secret");

    /// <summary>Manually-advanced clock so token expiry is deterministic in tests.</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static (CachedFhirTokenService sut, CountingHttpMessageHandler handler) CreateSut(
        int expiresIn = 3600, TimeProvider? clock = null)
    {
        var handler = new CountingHttpMessageHandler(_ => CountingHttpMessageHandler.TokenResponse("tok", expiresIn));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(() => new HttpClient(handler, disposeHandler: false));

        var creds = new Mock<IClientCredentialProvider>();
        creds.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Cred);

        var sut = new CachedFhirTokenService(factory.Object, creds.Object, NullLogger<CachedFhirTokenService>.Instance, clock);
        return (sut, handler);
    }

    [Fact]
    public async Task SameClientAndScope_SecondCall_IsCached()
    {
        var (sut, handler) = CreateSut();

        var t1 = await sut.GetAccessTokenAsync("client-a", "ship");
        var t2 = await sut.GetAccessTokenAsync("client-a", "ship");

        Assert.Equal("tok", t1);
        Assert.Equal("tok", t2);
        Assert.Equal(1, handler.CallCount); // only one token request
    }

    [Fact]
    public async Task DifferentScope_IsCachedSeparately()
    {
        var (sut, handler) = CreateSut();

        await sut.GetAccessTokenAsync("client-a", "ship");
        await sut.GetAccessTokenAsync("client-a", "ship-full-access");

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DifferentClient_IsCachedSeparately()
    {
        var (sut, handler) = CreateSut();

        await sut.GetAccessTokenAsync("client-a", "ship");
        await sut.GetAccessTokenAsync("client-b", "ship");

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExpiredToken_IsRefetched_FreshIsCached()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var (sut, handler) = CreateSut(expiresIn: 60, clock: clock); // 60s TTL, 30s refresh margin

        await sut.GetAccessTokenAsync("client-a", "ship");
        await sut.GetAccessTokenAsync("client-a", "ship");
        Assert.Equal(1, handler.CallCount); // still well within TTL → cached

        clock.Advance(TimeSpan.FromSeconds(31)); // now inside the refresh margin
        await sut.GetAccessTokenAsync("client-a", "ship");
        Assert.Equal(2, handler.CallCount); // refetched
    }

    [Fact]
    public async Task ConcurrentCalls_SameKey_SingleFlight_FetchOnce()
    {
        var (sut, handler) = CreateSut();
        handler.Delay = TimeSpan.FromMilliseconds(100); // widen the race window

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => sut.GetAccessTokenAsync("client-a", "ship"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("tok", r));
        Assert.Equal(1, handler.CallCount); // single-flight collapsed the stampede
    }

    [Fact]
    public async Task BlankClientId_Throws()
    {
        var (sut, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAccessTokenAsync("", "ship"));
    }
}
