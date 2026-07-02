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

        /// <summary>Max delivery attempts before a callback is dead-lettered (CallbackStatus = "Failed").</summary>
        public int MaxAttempts { get; set; } = 8;

        public ProxyOptions Proxy { get; set; } = new();

        public CallbackValidationOptions Validation { get; set; } = new();
    }

    /// <summary>
    /// Controls validation of the (stored) EMR callback URL before delivery (SSRF / open-redirect guard).
    /// <para>
    /// Default is <see cref="Enabled"/> = <c>false</c> → URLs are trusted (current behaviour; see
    /// docs/multi-client/SECURITY-NOTE-callback-ssrf.md). DevOps can enable host allow-listing without a
    /// code change. Scheme sanity (absolute http/https) is always enforced.
    /// </para>
    /// </summary>
    public sealed class CallbackValidationOptions
    {
        public bool Enabled { get; set; } = false;
        public bool RequireHttps { get; set; } = false;

        /// <summary>Global allowed hosts. Case-insensitive; a leading "*." matches any subdomain.</summary>
        public string[] AllowedHosts { get; set; } = Array.Empty<string>();

        /// <summary>Optional per-client allowed hosts (key = clientId); overrides the global list for that client.</summary>
        public Dictionary<string, string[]> PerClient { get; set; } = new();
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
