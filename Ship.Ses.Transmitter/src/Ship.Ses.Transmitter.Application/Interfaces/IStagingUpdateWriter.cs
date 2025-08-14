using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    public interface IStagingUpdateWriter
    {
        Task BulkMarkSubmittedAsync(IEnumerable<StagingTransmissionMark> marks, CancellationToken ct);
        Task BulkMarkFailedAsync(IEnumerable<long> stagingIds, CancellationToken ct);
    }

    public sealed record StagingTransmissionMark(long StagingId, string? ShipSubmitTxId, DateTime SubmittedAtUtc);
}
