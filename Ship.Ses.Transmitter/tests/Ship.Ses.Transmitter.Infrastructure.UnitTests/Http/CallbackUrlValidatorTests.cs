using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ship.Ses.Transmitter.Infrastructure.Http;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Http;

public class CallbackUrlValidatorTests
{
    private static CallbackUrlValidator CreateSut(CallbackValidationOptions validation)
    {
        var monitor = new Mock<IOptionsMonitor<EmrCallbackOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new EmrCallbackOptions { Validation = validation });
        return new CallbackUrlValidator(monitor.Object, NullLogger<CallbackUrlValidator>.Instance);
    }

    [Fact]
    public void TrustMode_Disabled_AllowsAnyHttpsUrl()
    {
        var ok = CreateSut(new CallbackValidationOptions { Enabled = false })
            .IsAllowed("https://anything.example.org/cb", "client-a", out var reason);

        Assert.True(ok);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://host/x")]
    public void AlwaysRejects_MissingOrNonHttp(string? url)
    {
        var ok = CreateSut(new CallbackValidationOptions { Enabled = false })
            .IsAllowed(url, "client-a", out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Fact]
    public void RequireHttps_RejectsHttp_EvenInTrustMode()
    {
        var ok = CreateSut(new CallbackValidationOptions { Enabled = false, RequireHttps = true })
            .IsAllowed("http://host/cb", "client-a", out var reason);

        Assert.False(ok);
        Assert.Contains("https", reason);
    }

    [Fact]
    public void Enabled_AllowsHostOnGlobalList()
    {
        var sut = CreateSut(new CallbackValidationOptions { Enabled = true, AllowedHosts = new[] { "emr.example.org" } });

        Assert.True(sut.IsAllowed("https://emr.example.org/cb", "client-a", out _));
        Assert.False(sut.IsAllowed("https://evil.test/cb", "client-a", out var reason));
        Assert.Contains("not in the allow-list", reason);
    }

    [Fact]
    public void Enabled_WildcardMatchesSubdomainsOnly()
    {
        var sut = CreateSut(new CallbackValidationOptions { Enabled = true, AllowedHosts = new[] { "*.example.org" } });

        Assert.True(sut.IsAllowed("https://emr.example.org/cb", "c", out _));
        Assert.False(sut.IsAllowed("https://example.org/cb", "c", out _)); // apex not matched by *.
        Assert.False(sut.IsAllowed("https://evil.org/cb", "c", out _));
    }

    [Fact]
    public void Enabled_PerClientList_OverridesGlobal()
    {
        var sut = CreateSut(new CallbackValidationOptions
        {
            Enabled = true,
            AllowedHosts = new[] { "global.example.org" },
            PerClient = new Dictionary<string, string[]> { ["client-a"] = new[] { "a.example.org" } }
        });

        Assert.True(sut.IsAllowed("https://a.example.org/cb", "client-a", out _));     // per-client host
        Assert.False(sut.IsAllowed("https://global.example.org/cb", "client-a", out _)); // global doesn't apply to client-a
        Assert.True(sut.IsAllowed("https://global.example.org/cb", "client-b", out _));  // client-b falls back to global
    }

    [Fact]
    public void Enabled_NoAllowedHostsConfigured_Denies()
    {
        var ok = CreateSut(new CallbackValidationOptions { Enabled = true })
            .IsAllowed("https://emr.example.org/cb", "client-a", out var reason);

        Assert.False(ok);
        Assert.Contains("no allowed hosts", reason);
    }
}
