using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi.Models
{
    public sealed class SyncClientStatusWriteDto
    {
        public string? Status { get; init; }
        public DateTime? LastCheckIn { get; init; }
        public DateTime? LastSyncedAt { get; init; }
        public int? TotalSynced { get; init; }      // deltas or partials; server accumulates
        public int? TotalFailed { get; init; }
        public string? CurrentBatchId { get; init; }
        public string? LastError { get; init; }
        public string? IpAddress { get; init; }
        public string? Hostname { get; init; }
        public string? Version { get; init; }
        public string? SignatureHash { get; init; }
    }
}
