using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi.Models
{
    public sealed class SyncClientMetricWriteDto
    {
        public required string ResourceType { get; init; }
        public required DateTime SyncWindowStart { get; init; }
        public required DateTime SyncWindowEnd { get; init; }
        public int SyncedCount { get; init; }
        public int FailedCount { get; init; }
        public required string BatchId { get; init; }
        public string? Notes { get; init; }
    }
}
