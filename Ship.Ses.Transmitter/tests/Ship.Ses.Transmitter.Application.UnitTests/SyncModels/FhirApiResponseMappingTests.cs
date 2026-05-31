using System.Text.Json;
using Ship.Ses.Transmitter.Domain.SyncModels;

namespace Ship.Ses.Transmitter.Application.UnitTests.SyncModels;

/// <summary>
/// Characterises how a SHIP FHIR response body deserialises into <see cref="FhirApiResponse"/> —
/// the parse <see cref="Ship.Ses.Transmitter.Infrastructure.Services.FhirApiService"/> relies on to
/// drive sync bookkeeping. Covers the <see cref="FlexibleBundleConverter"/> tolerance for the
/// <c>data</c> shape (array of items, missing, single object).
/// </summary>
public class FhirApiResponseMappingTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    private static FhirApiResponse? Deserialize(string json) =>
        JsonSerializer.Deserialize<FhirApiResponse>(json, Opts);

    [Fact]
    public void ParsesStatusCodeMessageAndTransactionId()
    {
        var r = Deserialize("""
            {"status":"success","code":202,"message":"Request accepted","transactionId":"txn-9"}
            """);

        Assert.NotNull(r);
        Assert.Equal("success", r!.Status);
        Assert.Equal(202, r.Code);
        Assert.Equal("Request accepted", r.Message);
        Assert.Equal("txn-9", r.transactionId);
    }

    [Fact]
    public void ParsesDataArray_IntoBundleItems()
    {
        var r = Deserialize("""
            {"status":"success","code":200,"data":[
                {"id":"a1","transactionId":"t1","status":"ok","message":"created"},
                {"id":"a2","transactionId":"t2","status":"ok","message":"created"}
            ]}
            """);

        Assert.NotNull(r!.Data);
        Assert.Equal(2, r.Data!.Count);
        Assert.Equal("a1", r.Data[0].Id);
        Assert.Equal("t2", r.Data[1].TransactionId);
    }

    [Fact]
    public void MissingData_IsNull()
    {
        var r = Deserialize("""{"status":"success","code":200,"message":"OK"}""");

        Assert.NotNull(r);
        Assert.Null(r!.Data);
    }

    [Fact]
    public void DataAsSingleObject_FallsBackToNull_WithoutThrowing()
    {
        // SHIP sometimes returns a single object rather than an array; the flexible converter
        // tolerates it (returns null) instead of failing the whole parse.
        var r = Deserialize("""{"status":"success","code":200,"data":{"id":"only"}}""");

        Assert.NotNull(r);
        Assert.Null(r!.Data);
    }

    [Fact]
    public void NullData_IsNull()
    {
        var r = Deserialize("""{"status":"error","code":400,"data":null}""");

        Assert.NotNull(r);
        Assert.Null(r!.Data);
    }
}
