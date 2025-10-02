using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.AdminApi.Models
{
    public sealed class MetricsBulkRequestDto
    {
        public required List<SyncClientMetricWriteDto> Items { get; init; }
    }
}
