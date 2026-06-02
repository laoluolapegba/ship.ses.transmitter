using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

/// <summary>
/// Pins the startup-load model (mirrors the SeS Ingestor): clients are discovered by listing the prefix
/// and read once into memory; only active, non-revoked clients with a usable secret are loaded; the Vault
/// folder name is the clientId; Vault must be configured (env) or startup fails.
/// </summary>
public class VaultClientCredentialProviderTests
{
    private static readonly AuthSettings AuthDefaults = new()
    {
        TokenEndpoint = "https://identity/token",
        GrantType = "client_credentials"
    };

    private static VaultClientSecretSettings Configured(VaultClientSecretSettings? s = null) =>
        (s ?? new VaultClientSecretSettings()) with { Address = "https://vault.local", Token = "t" };

    private static (VaultClientCredentialProvider sut, Mock<IVaultSecretReader> reader) CreateSut(VaultClientSecretSettings? settings = null)
    {
        var reader = new Mock<IVaultSecretReader>();
        var sut = new VaultClientCredentialProvider(
            reader.Object, settings ?? Configured(), Options.Create(AuthDefaults),
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
    public async Task Initialize_LoadsClient_MergesNonSecretDefaults_FolderNameIsClientId()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "lakeshore");
        // A clientId field in the secret is ignored — the folder name is authoritative (the Ingestor way).
        SetupRead(reader, "ses/clients/lakeshore", Secret(("clientSecret", "s3cr3t"), ("clientId", "ignored-oidc")));

        await sut.InitializeAsync();
        var cred = await sut.GetAsync("lakeshore");

        Assert.True(sut.IsClientKnown("lakeshore"));
        Assert.Equal("https://identity/token", cred.TokenEndpoint); // non-secret default
        Assert.Equal("client_credentials", cred.GrantType);          // non-secret default
        Assert.Equal("s3cr3t", cred.ClientSecret);                   // from Vault
        Assert.Equal("lakeshore", cred.ClientId);                    // folder name, not the secret's clientId field
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

    [Fact]
    public async Task Initialize_HonorsConfiguredSecretKey()
    {
        var (sut, reader) = CreateSut(Configured(new VaultClientSecretSettings { SecretKey = "hmac" }));
        SetupList(reader, "c");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("hmac", "hmac-key")));

        await sut.InitializeAsync();

        Assert.Equal("hmac-key", (await sut.GetAsync("c")).ClientSecret);
    }

    [Fact]
    public async Task Initialize_MissingSecretField_SkipsClient()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "c");
        reader.Setup(r => r.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Secret(("somethingElse", "x"))); // no clientSecret

        await sut.InitializeAsync();

        Assert.False(sut.IsClientKnown("c"));
    }

    [Fact]
    public async Task Initialize_SkipsClientsWithUnreadableSecret()
    {
        var (sut, reader) = CreateSut();
        SetupList(reader, "ok", "missing");
        SetupRead(reader, "ses/clients/ok", Secret(("clientSecret", "s")));
        SetupRead(reader, "ses/clients/missing", null);

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
    public async Task Initialize_NotConfigured_Throws()
    {
        // No VAULT_ADDR / VAULT_TOKEN → fail fast at startup.
        var (sut, _) = CreateSut(new VaultClientSecretSettings());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitializeAsync());
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
