using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Domain.Sync
{
    public class SyncResultDto
    {
        public int Total { get; set; }
        public int Synced { get; set; }

        /// <summary>Records that failed this cycle but were requeued (still Pending) for a later retry.</summary>
        public int Requeued { get; set; }

        /// <summary>Records that exhausted their retry budget and are now permanently Failed.</summary>
        public int Failed { get; set; }
        public List<string> FailedIds { get; set; } = new();

    }
}
