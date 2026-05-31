using Ship.Ses.Transmitter.Application.Interfaces;

namespace Ship.Ses.Transmitter.Application.UnitTests.Interfaces;

/// <summary>
/// Pins the <see cref="ClientCredential"/> contract: it carries the per-client credential material
/// (endpoint + clientId/secret + grant type) and intentionally excludes scope, which is route-derived.
/// (FINDINGS.md §2.4; Build Plan Phase 2.)
/// </summary>
public class ClientCredentialContractTests
{
    [Fact]
    public void GrantType_DefaultsToClientCredentials()
    {
        var cred = new ClientCredential("https://identity/token", "lakeshore", "secret");

        Assert.Equal("client_credentials", cred.GrantType);
    }

    [Fact]
    public void ExposesEndpointClientIdAndSecret()
    {
        var cred = new ClientCredential("https://identity/token", "lakeshore", "secret", "password");

        Assert.Equal("https://identity/token", cred.TokenEndpoint);
        Assert.Equal("lakeshore", cred.ClientId);
        Assert.Equal("secret", cred.ClientSecret);
        Assert.Equal("password", cred.GrantType);
    }

    [Fact]
    public void ValueEquality_SameMaterial_IsEqual()
    {
        // Record value semantics matter: the token cache and tests compare credentials by value.
        var a = new ClientCredential("https://identity/token", "lakeshore", "secret");
        var b = new ClientCredential("https://identity/token", "lakeshore", "secret");

        Assert.Equal(a, b);
    }

    [Fact]
    public void ValueEquality_DifferentSecret_IsNotEqual()
    {
        var a = new ClientCredential("https://identity/token", "lakeshore", "secret-1");
        var b = new ClientCredential("https://identity/token", "lakeshore", "secret-2");

        Assert.NotEqual(a, b);
    }
}
