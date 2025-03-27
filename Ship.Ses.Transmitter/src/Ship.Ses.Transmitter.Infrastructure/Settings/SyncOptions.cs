using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public class SyncOptions
    {
        public ResourceSyncSettings Patient { get; set; }
        public ResourceSyncSettings Encounter { get; set; }
    }
}
