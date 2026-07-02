using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Queue;

namespace Ship.Ses.Transmitter.Domain.UnitTests.Patients;

/// <summary>
/// Characterization tests for the concrete <see cref="FhirSyncRecord"/> types.
/// These pin the collection/resource mapping the workers rely on to route records.
/// </summary>
public class FhirSyncRecordTests
{
    [Fact]
    public void PatientSyncRecord_Defaults_MapToPatientCollection()
    {
        var record = new PatientSyncRecord();

        Assert.Equal("Patient", record.ResourceType);
        Assert.Equal("transformed_pool_patients", record.CollectionName);
    }

    [Fact]
    public void GenericResourceSyncRecord_UsesSharedResourcePool_AndEmptyResourceTypeByDefault()
    {
        var record = new GenericResourceSyncRecord();

        Assert.Equal("transformed_pool_resources", record.CollectionName);
        Assert.Equal(string.Empty, record.ResourceType);
    }

    [Fact]
    public void NewRecord_Defaults_ArePendingWithZeroRetries()
    {
        var record = new PatientSyncRecord();

        Assert.Equal("Pending", record.Status);
        Assert.Equal(0, record.RetryCount);
        Assert.Null(record.TimeSynced);
    }
}
