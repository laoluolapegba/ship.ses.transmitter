using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public sealed class StatusProbeSettings
    {
        public bool Enabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 120;
        public int PollIntervalSeconds { get; set; } = 30;
        public int MaxAttempts { get; set; } = 3;
        public int BatchSize { get; set; } = 50;
    }

}
