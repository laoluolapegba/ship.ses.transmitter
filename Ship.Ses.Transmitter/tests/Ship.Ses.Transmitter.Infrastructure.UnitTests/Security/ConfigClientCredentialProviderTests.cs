using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

public class ConfigClientCredentialProviderTests
{
    private static ConfigClientCredentialProvider CreateSut(AuthSettings auth) =>
        new(Options.Create(auth), NullLogger<ConfigClientCredentialProvider>.Instance);

    private static readonly AuthSettings Auth = new()
    {
        TokenEndpoint = "https://identity/token",
        ClientId = "lakeshore",
        ClientSecret = "secret",
        GrantType = "client_credentials"
    };

    [Fact]
    public async Task GetAsync_ReturnsConfiguredCredential()
    {
        var cred = await CreateSut(Auth).GetAsync("any-client");

        Assert.Equal("https://identity/token", cred.TokenEndpoint);
        Assert.Equal("lakeshore", cred.ClientId);
        Assert.Equal("secret", cred.ClientSecret);
        Assert.Equal("client_credentials", cred.GrantType);
    }

    [Fact]
    public async Task GetAsync_SingleClientFallback_IgnoresClientIdForNow()
    {
        var sut = CreateSut(Auth);

        var a = await sut.GetAsync("client-a");
        var b = await sut.GetAsync("client-b");

        // Interim behaviour: every clientId resolves to the same configured credential.
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task GetAsync_BlankGrantType_DefaultsToClientCredentials()
    {
        var sut = CreateSut(new AuthSettings { TokenEndpoint = "https://x", ClientId = "c", ClientSecret = "s", GrantType = "" });

        var cred = await sut.GetAsync("c");

        Assert.Equal("client_credentials", cred.GrantType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_BlankClientId_Throws(string clientId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateSut(Auth).GetAsync(clientId));
    }

    [Fact]
    public async Task InitializeAsync_IsNoOp_AndEveryNonBlankClientIsKnown()
    {
        var sut = CreateSut(Auth);
        await sut.InitializeAsync(); // single-client fallback: nothing to discover

        Assert.True(sut.IsClientKnown("any-client"));
        Assert.False(sut.IsClientKnown(""));
        Assert.False(sut.IsClientKnown("   "));
    }
}
