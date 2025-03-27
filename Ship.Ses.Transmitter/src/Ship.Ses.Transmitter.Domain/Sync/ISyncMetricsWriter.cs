using Ship.Ses.Transmitter.Domain;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    public interface ISyncMetricsWriter
    {
        Task WriteStatusAsync(SyncClientStatus status);
        Task WriteMetricAsync(SyncClientMetric metric);
    }
}