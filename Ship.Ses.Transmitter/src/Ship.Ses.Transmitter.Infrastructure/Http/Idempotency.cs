using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Http
{

    public static class Idempotency
    {
        public static string NewKey() => Guid.NewGuid().ToString("n");
    }

}
