using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Sync
{
    public interface ISyncMetricsWriter
    {
        Task WriteStatusAsync(SyncClientStatus status);
        Task WriteMetricAsync(SyncClientMetric metric);
        Task WriteMetricsAsync(IEnumerable<SyncClientMetric> metrics);
    }
}