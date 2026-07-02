using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

/// <summary>
/// Pins the config/env-backed credential model: clients come from AppSettings:Clients (secret values are
/// ISW-injected into environment variables and bound over the placeholders); only ACTIVE clients with a
/// usable secret are loaded; non-secret material (token endpoint, grant type) comes from AuthSettings; and
/// there are no Vault API calls, address, token, or X-Vault-Token anywhere in the path.
/// </summary>
public class ConfigClientCredentialProviderTests
{
    private static readonly AuthSettings AuthDefaults = new()
    {
        TokenEndpoint = "https://identity/token",
        GrantType = "client_credentials"
    };

    private static ConfigClientCredentialProvider CreateSut(params ClientCredentialEntry[] clients)
    {
        var directory = new ClientDirectoryOptions { Clients = new List<ClientCredentialEntry>(clients) };
        return new ConfigClientCredentialProvider(
            Options.Create(directory),
            Options.Create(AuthDefaults),
            NullLogger<ConfigClientCredentialProvider>.Instance);
    }

    private static ClientCredentialEntry Entry(string id, string? secret = "s3cr3t", string? hmac = "hmac-key", string status = "ACTIVE") =>
        new() { ClientId = id, ClientSecret = secret, HmacSecret = hmac, Status = status };

    [Fact]
    public async Task Initialize_LoadsActiveClient_MergesNonSecretDefaults_AndHmacSecret()
    {
        var sut = CreateSut(Entry("ses-client-a", secret: "s3cr3t", hmac: "hmac-key"));

        await sut.InitializeAsync();
        var cred = await sut.GetAsync("ses-client-a");

        Assert.True(sut.IsClientKnown("ses-client-a"));
        Assert.Equal("https://identity/token", cred.TokenEndpoint); // non-secret default from AuthSettings
        Assert.Equal("client_credentials", cred.GrantType);          // non-secret default from AuthSettings
        Assert.Equal("ses-client-a", cred.ClientId);
        Assert.Equal("s3cr3t", cred.ClientSecret);                   // per-client, injected
        Assert.Equal("hmac-key", cred.HmacSecret);                   // per-client HMAC key, injected
    }

    [Fact]
    public async Task Initialize_LoadsAllActiveClients()
    {
        var sut = CreateSut(Entry("a"), Entry("b"), Entry("c"));

        await sut.InitializeAsync();

        Assert.Equal(3, sut.Count);
        Assert.True(sut.IsClientKnown("a"));
        Assert.True(sut.IsClientKnown("b"));
        Assert.True(sut.IsClientKnown("c"));
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("REVOKED")]
    [InlineData("")]
    public async Task Initialize_SkipsNonActiveClients(string status)
    {
        var sut = CreateSut(Entry("c", status: status));

        await sut.InitializeAsync();

        Assert.False(sut.IsClientKnown("c"));
    }

    [Fact]
    public async Task Initialize_IsCaseInsensitiveOnActiveStatus()
    {
        var sut = CreateSut(Entry("c", status: "active"));

        await sut.InitializeAsync();

        Assert.True(sut.IsClientKnown("c"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Initialize_SkipsClientWithoutInjectedSecret(string? secret)
    {
        // Placeholder never overridden by the ISW-injected env var → treated as missing.
        var sut = CreateSut(Entry("c", secret: secret));

        await sut.InitializeAsync();

        Assert.False(sut.IsClientKnown("c"));
    }

    [Fact]
    public async Task Initialize_SkipsEntryWithoutClientId()
    {
        var sut = CreateSut(new ClientCredentialEntry { ClientId = "", ClientSecret = "s" });

        await sut.InitializeAsync();

        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public async Task Initialize_NoClients_LoadsNothing()
    {
        var sut = CreateSut();

        await sut.InitializeAsync();

        Assert.Equal(0, sut.Count);
        Assert.False(sut.IsClientKnown("anyone"));
    }

    [Fact]
    public async Task GetAsync_UnknownClient_Throws()
    {
        var sut = CreateSut(Entry("known"));
        await sut.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("nope"));
    }

    [Fact]
    public async Task GetAsync_BlankClientId_Throws()
    {
        var sut = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(""));
    }
}
