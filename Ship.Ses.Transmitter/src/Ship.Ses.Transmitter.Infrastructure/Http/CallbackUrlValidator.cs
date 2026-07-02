using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Http
{
    /// <summary>
    /// Validates an EMR callback URL before delivery (SSRF / open-redirect guard, Finding 5.1).
    /// </summary>
    public interface ICallbackUrlValidator
    {
        /// <summary>Returns true if <paramref name="url"/> may be called for <paramref name="clientId"/>.</summary>
        bool IsAllowed(string? url, string? clientId, out string? reason);
    }

    /// <summary>
    /// Config-driven validator. Scheme sanity (absolute http/https) is always enforced. Host
    /// allow-listing is opt-in via <see cref="CallbackValidationOptions.Enabled"/> (default off = trust),
    /// using a per-client list when present, else the global list. A leading "*." matches subdomains.
    /// </summary>
    public sealed class CallbackUrlValidator : ICallbackUrlValidator
    {
        private readonly IOptionsMonitor<EmrCallbackOptions> _options;
        private readonly ILogger<CallbackUrlValidator> _log;

        public CallbackUrlValidator(IOptionsMonitor<EmrCallbackOptions> options, ILogger<CallbackUrlValidator> log)
        {
            _options = options;
            _log = log;
        }

        public bool IsAllowed(string? url, string? clientId, out string? reason)
        {
            var validation = _options.CurrentValue.Validation ?? new CallbackValidationOptions();

            if (string.IsNullOrWhiteSpace(url))
            {
                reason = "Callback URL is missing";
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                reason = "Callback URL is not an absolute URI";
                return false;
            }

            var isHttp = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            if (!isHttp)
            {
                reason = $"Callback URL scheme '{uri.Scheme}' is not http/https";
                return false;
            }

            if (validation.RequireHttps && uri.Scheme != Uri.UriSchemeHttps)
            {
                reason = "Callback URL must use https";
                return false;
            }

            if (!validation.Enabled)
            {
                // Trust mode (default): scheme-sane URLs are accepted. See SECURITY-NOTE-callback-ssrf.md.
                reason = null;
                return true;
            }

            var allowed = (!string.IsNullOrWhiteSpace(clientId)
                           && validation.PerClient is not null
                           && validation.PerClient.TryGetValue(clientId, out var perClient)
                           && perClient is { Length: > 0 })
                ? perClient
                : validation.AllowedHosts ?? Array.Empty<string>();

            if (allowed.Length == 0)
            {
                reason = $"Callback validation is enabled but no allowed hosts are configured for client '{clientId ?? "(none)"}'";
                return false;
            }

            if (allowed.Any(h => HostMatches(uri.Host, h)))
            {
                reason = null;
                return true;
            }

            reason = $"Callback host '{uri.Host}' is not in the allow-list for client '{clientId ?? "(none)"}'";
            return false;
        }

        private static bool HostMatches(string host, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..]; // ".example.com"
                return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                       && host.Length > suffix.Length;
            }

            return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
