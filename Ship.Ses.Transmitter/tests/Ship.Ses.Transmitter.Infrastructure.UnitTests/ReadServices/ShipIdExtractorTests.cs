using Ship.Ses.Transmitter.Infrastructure.ReadServices;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.ReadServices;

/// <summary>
/// Pins SHIP-ID extraction from a probe response: the value comes from data.identifier[] — the entry whose
/// type.coding[].code is SHIP_ID — so a probe-driven EMR callback carries the same shipId a real SHIP
/// callback would, instead of the blank seeded value.
/// </summary>
public class ShipIdExtractorTests
{
    private const string EnvelopeWithShipId = """
    {
      "status": "SUCCESS",
      "code": 200,
      "message": "OK",
      "data": {
        "resourceType": "Patient",
        "id": "pat-55",
        "identifier": [
          { "system": "https://emr.local/mrn", "value": "MRN-123" },
          {
            "type": { "coding": [ { "system": "https://ship/iddt", "code": "SHIP_ID" } ] },
            "value": "SHIP-999"
          }
        ]
      },
      "transactionId": "txn-7f3a"
    }
    """;

    [Fact]
    public void Extracts_ShipId_FromDataIdentifierByTypeCodingCode()
    {
        Assert.Equal("SHIP-999", ShipIdExtractor.TryExtractShipId(EnvelopeWithShipId));
    }

    [Fact]
    public void CodeMatch_IsCaseInsensitive()
    {
        var json = """
        { "data": { "identifier": [ { "type": { "coding": [ { "code": "ship_id" } ] }, "value": "SHIP-1" } ] } }
        """;
        Assert.Equal("SHIP-1", ShipIdExtractor.TryExtractShipId(json));
    }

    [Fact]
    public void ScansBundle_WhenDataIsArray()
    {
        var json = """
        { "data": [
            { "resourceType": "Observation", "identifier": [ { "system": "x", "value": "y" } ] },
            { "resourceType": "Patient", "identifier": [ { "type": { "coding": [ { "code": "SHIP_ID" } ] }, "value": "SHIP-2" } ] }
        ] }
        """;
        Assert.Equal("SHIP-2", ShipIdExtractor.TryExtractShipId(json));
    }

    [Fact]
    public void FallsBackToRoot_WhenNoDataWrapper()
    {
        var json = """
        { "resourceType": "Patient", "identifier": [ { "type": { "coding": [ { "code": "SHIP_ID" } ] }, "value": "SHIP-3" } ] }
        """;
        Assert.Equal("SHIP-3", ShipIdExtractor.TryExtractShipId(json));
    }

    [Fact]
    public void Null_WhenNoShipIdCodedIdentifier()
    {
        var json = """
        { "data": { "identifier": [ { "system": "https://emr.local/mrn", "value": "MRN-123" } ] } }
        """;
        Assert.Null(ShipIdExtractor.TryExtractShipId(json));
    }

    [Fact]
    public void Null_WhenShipIdIdentifierHasNoValue()
    {
        var json = """
        { "data": { "identifier": [ { "type": { "coding": [ { "code": "SHIP_ID" } ] } } ] } }
        """;
        Assert.Null(ShipIdExtractor.TryExtractShipId(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{ \"data\": { } }")]
    [InlineData("{ \"data\": { \"identifier\": \"not-an-array\" } }")]
    public void Null_ForBlankMalformedOrMissing(string? raw)
    {
        Assert.Null(ShipIdExtractor.TryExtractShipId(raw));
    }
}
