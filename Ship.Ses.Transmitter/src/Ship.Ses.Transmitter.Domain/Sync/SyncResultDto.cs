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
        public int Failed { get; set; }
        public List<string> FailedIds { get; set; } = new();

    }
}
