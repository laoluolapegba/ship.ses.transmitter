using Ship.Ses.Transmitter.Domain;
using System.Collections.Generic;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    public interface ISyncMetricsCollector
    {
        SyncClientStatus CollectStatus(string clientId);
        IEnumerable<SyncClientMetric> CollectMetrics(string clientId);
    }
}