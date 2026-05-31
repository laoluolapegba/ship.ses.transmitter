using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Domain.SyncModels;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.ReadServices;

/// <summary>
/// Pins the success/fail bookkeeping in <see cref="FhirSyncService.ProcessPendingRecordsAsync{T}"/>.
/// Regression guard for FINDINGS.md §3.6: a rejected record must NOT also be marked Synced.
/// </summary>
public class FhirSyncServiceTests
{
    private readonly Mock<IFhirSyncStore> _repo = new();
    private readonly Mock<IFhirApiService> _api = new();
    private readonly Mock<IStagingUpdateWriter> _staging = new();
    private readonly Mock<IOptionsMonitor<FhirRoutingSettings>> _routing = new();

    private readonly List<(string status, string message)> _persisted = new();

    public FhirSyncServiceTests()
    {
        _routing.Setup(m => m.CurrentValue).Returns(new FhirRoutingSettings
        {
            Default = new FhirRouteSettings { BaseUrl = "https://gateway/fhir", CallbackUrlTemplate = "https://cb/" }
        });

        _repo.Setup(r => r.InsertStatusEventAsync(It.IsAny<StatusEvent>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        // Capture every status the service tries to persist so we can assert on it.
        _repo.Setup(r => r.BulkUpdateStatusAsync<PatientSyncRecord>(
                 It.IsAny<IReadOnlyDictionary<string, RecordStatusUpdate>>()))
             .Callback<IReadOnlyDictionary<string, RecordStatusUpdate>>(
                 d => _persisted.AddRange(d.Values.Select(v => (v.Status, v.Message))))
             .Returns(Task.CompletedTask);

        _staging.Setup(s => s.BulkMarkSubmittedAsync(It.IsAny<IEnumerable<StagingTransmissionMark>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        _staging.Setup(s => s.BulkMarkFailedAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
    }

    private FhirSyncService CreateSut() => new(
        _repo.Object,
        NullLogger<FhirSyncService>.Instance,
        _api.Object,
        _staging.Object,
        _routing.Object);

    private static PatientSyncRecord PendingPatient(string clientId = "client-a", string resourceId = "p1") => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        ResourceType = "Patient",
        ResourceId = resourceId,
        ClientId = clientId,
        Status = "Pending",
        FhirJson = new BsonDocument { { "resourceType", "Patient" }, { "id", resourceId } }
    };

    private void SetupPending(params PatientSyncRecord[] records) =>
        _repo.Setup(r => r.GetByStatusAsync<PatientSyncRecord>("Pending", It.IsAny<int>(), It.IsAny<int>()))
             .ReturnsAsync(records);

    private static FhirApiResponse Accepted(string txn = "txn-1") =>
        new() { Status = "success", Code = 202, Message = "Request accepted", transactionId = txn, Raw = "{}" };

    // Matches IFhirApiService.SendAsync(operation, clientId, resourceType, resourceId, jsonPayload, callbackUrl, shipService, ct)
    private Moq.Language.Flow.ISetup<IFhirApiService, Task<FhirApiResponse>> SetupSendForClient(string clientId) =>
        _api.Setup(a => a.SendAsync(
            It.IsAny<FhirOperation>(), clientId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));

    private Moq.Language.Flow.ISetup<IFhirApiService, Task<FhirApiResponse>> SetupSendAny() =>
        _api.Setup(a => a.SendAsync(
            It.IsAny<FhirOperation>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));

    [Fact]
    public async Task ProcessPendingRecords_WhenApiRejects_MarksFailedOnly_NotSynced()
    {
        SetupPending(PendingPatient());
        SetupSendAny().ReturnsAsync(new FhirApiResponse { Status = "error", Code = 400, Message = "bad request", Raw = "{}" });

        var result = await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Synced); // regression guard: rejected record was previously double-written as Synced
        Assert.DoesNotContain(_persisted, p => p.status == "Synced");
        Assert.Contains(_persisted, p => p.status == "Failed");
    }

    [Fact]
    public async Task ProcessPendingRecords_WhenApiAccepts_MarksSyncedOnly()
    {
        SetupPending(PendingPatient());
        SetupSendAny().ReturnsAsync(Accepted());

        var result = await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Synced);
        Assert.Equal(0, result.Failed);
        Assert.DoesNotContain(_persisted, p => p.status == "Failed");
    }

    [Fact]
    public async Task ProcessPendingRecords_NoPending_ReturnsEmptyResult()
    {
        SetupPending();

        var result = await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(_persisted);
    }

    [Fact]
    public async Task ProcessPendingRecords_SendsUnderEachRecordsOwnClientId()
    {
        SetupPending(PendingPatient(clientId: "client-x"));
        SetupSendAny().ReturnsAsync(Accepted());

        await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        // The record's own ClientId must be passed to the outbound call (not a global identity).
        _api.Verify(a => a.SendAsync(
            It.IsAny<FhirOperation>(), "client-x", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingRecords_OneClientFailing_DoesNotAffectAnotherClient()
    {
        SetupPending(
            PendingPatient(clientId: "good-client", resourceId: "g1"),
            PendingPatient(clientId: "bad-client", resourceId: "b1"));

        SetupSendForClient("good-client").ReturnsAsync(Accepted());
        SetupSendForClient("bad-client").ThrowsAsync(new HttpRequestException("token endpoint down"));

        var result = await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Synced); // good client unaffected by bad client's outage
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task ProcessPendingRecords_ClientBreaker_OpensAfterConsecutiveFailures_SkipsRest()
    {
        // 5 records for one client, all throwing → breaker should open after 3 and skip the rest.
        SetupPending(Enumerable.Range(1, 5)
            .Select(i => PendingPatient(clientId: "bad-client", resourceId: $"r{i}"))
            .ToArray());

        SetupSendForClient("bad-client").ThrowsAsync(new HttpRequestException("token endpoint down"));

        var result = await CreateSut().ProcessPendingRecordsAsync<PatientSyncRecord>(CancellationToken.None);

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Failed); // 3 real attempts + 2 skipped, all recorded as Failed
        Assert.Equal(0, result.Synced);

        // Only 3 outbound attempts were made before the breaker opened.
        _api.Verify(a => a.SendAsync(
            It.IsAny<FhirOperation>(), "bad-client", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
