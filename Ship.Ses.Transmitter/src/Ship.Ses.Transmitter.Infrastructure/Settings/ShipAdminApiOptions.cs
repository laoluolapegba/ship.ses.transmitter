using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Configuration
{
    public sealed class ShipAdminApiOptions
    {
        public string BaseUrl { get; set; } = default!;
        public int RequestTimeoutSeconds { get; set; } = 15;
        public int BulkMetricsMaxItems { get; set; } = 500;
        public RetryOptions Retry { get; set; } = new();
        public sealed class RetryOptions { public int MaxAttempts { get; set; } = 3; public int JitterMs { get; set; } = 250; }
    }
   

}
