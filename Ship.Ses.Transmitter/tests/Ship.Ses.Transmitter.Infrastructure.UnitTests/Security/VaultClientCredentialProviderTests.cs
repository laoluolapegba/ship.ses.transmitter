using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

/// <summary>
/// Pins the startup-load model: clients are discovered by listing the prefix and read once into memory.
/// Only active, non-revoked clients with a usable secret are loaded; there are no per-request Vault calls.
/// (Consistent with the Ingestor's VaultClientHmacCredentialLoader.)
/// </summary>
public class VaultClientCredentialProviderTests
{
    private static readonly AuthSettings AuthDefaults = new()
    {
        TokenEndpoint = "https://identity/token",
        GrantType = "client_credentials"
    };

    private static (VaultClientCredentialProvider sut, Mock<IVaultSecretReader> reader) CreateSut(VaultOptions? vault = null)
    {
        var reader = new Mock<IVaultSecretReader>();
        var opts = Options.Create(new ClientCredentialsOptions { Source = "Vault", Vault = vault ?? new VaultOptions() });
        var sut = new VaultClientCredentialProvider(
            reader.Object, opts, Options.Create(AuthDefaults),
            NullLogger<VaultClientCredentialProvider>.Instance);
        return (sut, reader);
    }

    private static IReadOnlyDictionary<string, string> Secret(params (string k, string v)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    private static void SetupList(Mock<IVaultSecretReader> reader, params string[] clientIds) =>
        reader.Setup(r => r.ListClientIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(clientIds);

    private static void SetupRead(Mock<IVaultSecretReader> reader, string path, IReadOnlyDictionary<string, string>? secret) =>
        reader.Setup(r => r.ReadAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(secret);

    [Fact]
    public async Task Initialize_LoadsClient_MergesNonSecretDefaults_AndAppliesClientIdKey()
    {
        var (sut, reader) = CreateSut(new VaultOptions { PathTemplate = "ses/clients/{clientId}/hmac" });
        SetupList(reader, "lakeshore");
        SetupRead(reader, "ses/clients/lakeshore/hmac", Secret(("clientSecret", "s3cr3t"), ("clientId", "lakeshore-oidc")));

        await sut.InitializeAsync();
        var cred = await sut.GetAsync("lakeshore");

        Assert.True(sut.IsClientKnown("lakeshore"));
        Assert.Equal("https://identity/token", cred.TokenEndpoint); // non-secret default
        Assert.Equal("client_credentials", cred.GrantType);          // non-secret default
        Assert.Equal("s3cr3t", cred.ClientSecret);                   // from Vault
        Assert.Equal("lakeshore-oidc", cred.ClientId);               // from Vault (ClientIdKey)
    }

    [Fact]
    public async Task Initialize_NoClientIdInSecret_DefaultsToFolderClientId()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "client-a");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "abc")));

        await sut.InitializeAsync();

        Assert.Equal("client-a", (await sut.GetAsync("client-a")).ClientId);
    }

    [Fact]
    public async Task Initialize_SecretKeyFallback_Hmac()
    {
        var (sut, reader) = CreateSut(new VaultOptions { SecretKey = "clientSecret" });
        SetupList(reader, "c");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("hmac", "hmac-key"))); // no clientSecret → falls back to hmac

        await sut.InitializeAsync();

        Assert.Equal("hmac-key", (await sut.GetAsync("c")).ClientSecret);
    }

    [Fact]
    public async Task Initialize_ReadsEachClientExactlyOnce_NoPerRequestCalls()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "c");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "s")));

        await sut.InitializeAsync();
        await sut.GetAsync("c");
        await sut.GetAsync("c"); // served from memory

        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("isActive", "false")]
    [InlineData("isRevoked", "true")]
    public async Task Initialize_SkipsInactiveOrRevokedClients(string key, string value)
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "c");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("clientSecret", "s"), (key, value)));

        await sut.InitializeAsync();

        Assert.False(sut.IsClientKnown("c"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("c"));
    }

    [Fact]
    public async Task Initialize_SkipsClientsWithUnreadableSecret()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "ok", "missing");
        SetupRead(reader, "ses/clients/ok/hmac", Secret(("clientSecret", "s")));
        SetupRead(reader, "ses/clients/missing/hmac", null);

        await sut.InitializeAsync();

        Assert.True(sut.IsClientKnown("ok"));
        Assert.False(sut.IsClientKnown("missing"));
    }

    [Fact]
    public async Task Initialize_NoClientsListed_LoadsNothing()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader); // empty

        await sut.InitializeAsync();

        Assert.False(sut.IsClientKnown("anyone"));
        reader.Verify(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_UnknownClient_Throws()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader); // none loaded
        await sut.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("nope"));
    }

    [Fact]
    public async Task GetAsync_BlankClientId_Throws()
    {
        var (sut, _) = CreateSut();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(""));
    }
}
