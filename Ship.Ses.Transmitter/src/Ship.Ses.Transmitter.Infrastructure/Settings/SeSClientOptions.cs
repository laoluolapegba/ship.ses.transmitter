using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public class SeSClientOptions
    {
        public string ClientId { get; set; }
        public string SyncEndpoint { get; set; }
        public int HeartbeatSeconds { get; set; } = 90;
        public int MetricsFlushSeconds { get; set; } = 300;
        public bool UseShipAdminApi { get; set; } = true;
    }
}
