using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public class FhirApiSettings
    {
        public string BaseUrl { get; set; }
        public string ClientCertPath { get; set; }
        public string ClientCertPassword { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
    }
}
