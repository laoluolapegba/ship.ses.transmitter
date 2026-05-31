using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

public class VaultClientCredentialProviderTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static readonly AuthSettings AuthDefaults = new()
    {
        TokenEndpoint = "https://identity/token",
        GrantType = "client_credentials"
    };

    private static (VaultClientCredentialProvider sut, Mock<IVaultSecretReader> reader, TestTimeProvider clock) CreateSut(
        VaultOptions? vault = null)
    {
        var reader = new Mock<IVaultSecretReader>();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var opts = Options.Create(new ClientCredentialsOptions { Source = "Vault", Vault = vault ?? new VaultOptions() });
        var sut = new VaultClientCredentialProvider(
            reader.Object, opts, Options.Create(AuthDefaults),
            NullLogger<VaultClientCredentialProvider>.Instance, clock);
        return (sut, reader, clock);
    }

    private static IReadOnlyDictionary<string, string> Secret(params (string k, string v)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [Fact]
    public async Task GetAsync_ReadsSecretByClientIdPath_AndMergesNonSecretDefaults()
    {
        var (sut, reader, _) = CreateSut(new VaultOptions { PathTemplate = "ses/clients/{clientId}/hmac" });
        reader.Setup(r => r.ReadAsync("ses/clients/lakeshore/hmac", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "s3cr3t"), ("clientId", "lakeshore-oidc")));

        var cred = await sut.GetAsync("lakeshore");

        Assert.Equal("https://identity/token", cred.TokenEndpoint); // non-secret default
        Assert.Equal("client_credentials", cred.GrantType);          // non-secret default
        Assert.Equal("s3cr3t", cred.ClientSecret);                   // from Vault
        Assert.Equal("lakeshore-oidc", cred.ClientId);               // from Vault (ClientIdKey)
    }

    [Fact]
    public async Task GetAsync_NoClientIdInSecret_DefaultsToRequestedClientId()
    {
        var (sut, reader, _) = CreateSut();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "abc")));

        var cred = await sut.GetAsync("client-a");

        Assert.Equal("client-a", cred.ClientId);
    }

    [Fact]
    public async Task GetAsync_SecretKeyFallback_Hmac()
    {
        var (sut, reader, _) = CreateSut(new VaultOptions { SecretKey = "clientSecret" });
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("hmac", "hmac-key"))); // no clientSecret → falls back to hmac

        var cred = await sut.GetAsync("c");

        Assert.Equal("hmac-key", cred.ClientSecret);
    }

    [Fact]
    public async Task GetAsync_CachesWithinTtl_ThenRefetchesAfterExpiry()
    {
        var (sut, reader, clock) = CreateSut(new VaultOptions { CacheTtlSeconds = 300 });
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "s")));

        await sut.GetAsync("c");
        await sut.GetAsync("c");
        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once); // cached

        clock.Advance(TimeSpan.FromSeconds(301));
        await sut.GetAsync("c");
        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2)); // refetched
    }

    [Fact]
    public async Task Invalidate_ForcesRefetch()
    {
        var (sut, reader, _) = CreateSut();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "s")));

        await sut.GetAsync("c");
        sut.Invalidate("c");
        await sut.GetAsync("c");

        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAsync_MissingSecret_Throws()
    {
        var (sut, reader, _) = CreateSut();
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IReadOnlyDictionary<string, string>?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("c"));
    }

    [Fact]
    public async Task GetAsync_BlankClientId_Throws()
    {
        var (sut, _, _) = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(""));
    }
}
