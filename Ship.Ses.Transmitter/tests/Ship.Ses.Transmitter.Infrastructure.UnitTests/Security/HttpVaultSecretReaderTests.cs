using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Infrastructure.UnitTests.Fakes;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Security;

public class HttpVaultSecretReaderTests
{
    private static HttpVaultSecretReader CreateSut(CountingHttpMessageHandler handler, VaultClientSecretSettings? settings = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
               .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://vault.local:8200") });

        return new HttpVaultSecretReader(factory.Object, settings ?? new VaultClientSecretSettings { Mount = "secret" }, NullLogger<HttpVaultSecretReader>.Instance);
    }

    private static HttpResponseMessage Kv2(string innerJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"data\":{{\"data\":{innerJson},\"metadata\":{{}}}}}}", Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ReadAsync_ParsesKvV2DataData()
    {
        HttpRequestMessage? captured = null;
        var handler = new CountingHttpMessageHandler(req => { captured = req; return Kv2("{\"clientSecret\":\"s3cr3t\",\"clientId\":\"lakeshore\"}"); });

        var result = await CreateSut(handler).ReadAsync("ses/clients/lakeshore");

        Assert.NotNull(result);
        Assert.Equal("s3cr3t", result!["clientSecret"]);
        Assert.Equal("lakeshore", result["clientId"]);
        // KV v2 path shape: /v1/{mount}/data/{path}
        Assert.Equal("https://vault.local:8200/v1/secret/data/ses/clients/lakeshore", captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ReadAsync_SendsVaultTokenHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new CountingHttpMessageHandler(req => { captured = req; return Kv2("{\"clientSecret\":\"x\"}"); });

        await CreateSut(handler, new VaultClientSecretSettings { Mount = "secret", Token = "vault-token-123" }).ReadAsync("p");

        Assert.True(captured!.Headers.TryGetValues("X-Vault-Token", out var values));
        Assert.Equal("vault-token-123", Assert.Single(values!));
    }

    [Fact]
    public async Task ReadAsync_NotFound_ReturnsNull()
    {
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateSut(handler).ReadAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task ListClientIdsAsync_ParsesKeys_AndHitsMetadataListPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new CountingHttpMessageHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"keys\":[\"lakeshore\",\"emr-b/\"]}}", Encoding.UTF8, "application/json")
            };
        });

        var result = await CreateSut(handler).ListClientIdsAsync("ses/clients");

        Assert.Equal(new[] { "lakeshore", "emr-b" }, result); // trailing slash trimmed
        // KV v2 list shape: /v1/{mount}/metadata/{prefix}?list=true
        Assert.Equal("https://vault.local:8200/v1/secret/metadata/ses/clients?list=true", captured!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListClientIdsAsync_Forbidden_ReturnsEmpty()
    {
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await CreateSut(handler).ListClientIdsAsync("ses/clients");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAsync_ServerError_Throws()
    {
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateSut(handler).ReadAsync("p"));
    }
}
