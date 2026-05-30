using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.Settings
{
    public sealed class EmrCallbackOptions
    {
        public int TimeoutSeconds { get; set; } = 30;
        public ProxyOptions Proxy { get; set; } = new();
    }

    public sealed class ProxyOptions
    {
        public bool Enabled { get; set; }
        public string[] HostEnvironments { get; set; } = Array.Empty<string>();
        public string? Address { get; set; }
        public bool BypassOnLocal { get; set; } = true;
        public string[] BypassList { get; set; } = Array.Empty<string>();
        public ProxyAuthOptions Auth { get; set; } = new();
    }

    public sealed class ProxyAuthOptions
    {
        // "None" | "Basic"
        public string Type { get; set; } = "None";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

}
