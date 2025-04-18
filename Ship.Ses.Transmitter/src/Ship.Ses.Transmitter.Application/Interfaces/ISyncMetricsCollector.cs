using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;
using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Application.Sync
{
    public interface ISyncMetricsCollector
    {
        Task<SyncClientStatus> CollectStatusAsync(string clientId);
        Task<IEnumerable<SyncClientMetric>> CollectMetricsAsync(string clientId);

        //IEnumerable<SyncClientMetric> CollectMetrics(string clientId);
    }
}