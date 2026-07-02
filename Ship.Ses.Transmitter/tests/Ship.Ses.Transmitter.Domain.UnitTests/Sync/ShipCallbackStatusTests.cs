using Ship.Ses.Transmitter.Domain.Sync;

namespace Ship.Ses.Transmitter.Domain.UnitTests.Sync;

/// <summary>
/// Pins the callback-eligibility contract: EVERY terminal MPI outcome (SUCCESS, ERROR, REJECTED, CONFLICT,
/// DUPLICATE) is deliverable to the EMR, and the non-terminal PENDING placeholder is not. This is what the
/// EmrCallbackWorker's fetch filter relies on so the EMR receives the final outcome, not only successes.
/// </summary>
public class ShipCallbackStatusTests
{
    [Theory]
    [InlineData("SUCCESS")]
    [InlineData("ERROR")]
    [InlineData("REJECTED")]
    [InlineData("CONFLICT")]
    [InlineData("DUPLICATE")]
    public void Terminal_ContainsEveryMpiOutcome(string status)
    {
        Assert.Contains(status, ShipCallbackStatus.Terminal);
        Assert.True(ShipCallbackStatus.IsTerminal(status));
    }

    [Fact]
    public void Terminal_IsExactlyTheFiveMpiOutcomes()
    {
        Assert.Equal(
            new[] { "SUCCESS", "ERROR", "REJECTED", "CONFLICT", "DUPLICATE" }.OrderBy(s => s),
            ShipCallbackStatus.Terminal.OrderBy(s => s));
    }

    [Fact]
    public void Pending_IsNotTerminal_AndNotDelivered()
    {
        Assert.False(ShipCallbackStatus.IsTerminal(ShipCallbackStatus.Pending));
        Assert.DoesNotContain(ShipCallbackStatus.Pending, ShipCallbackStatus.Terminal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("success")]   // case-sensitive: statuses are written in canonical upper-case
    [InlineData("UNKNOWN")]
    public void IsTerminal_False_ForBlankOrUnknownOrWrongCase(string? status)
    {
        Assert.False(ShipCallbackStatus.IsTerminal(status));
    }
}
