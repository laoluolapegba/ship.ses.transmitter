using global::Ship.Ses.Transmitter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Net;

namespace Ship.Ses.Transmitter.Infrastructure.Http
{


    public static class PollyPolicies
    {
        public static IAsyncPolicy<HttpResponseMessage> CreateStandardRetry(ShipAdminApiOptions opts)
        {
            var attempts = Math.Max(1, opts.Retry?.MaxAttempts ?? 3);
            var jitterMs = Math.Max(0, opts.Retry?.JitterMs ?? 250);

            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(
                    attempts,
                    attempt => TimeSpan.FromMilliseconds(200 * attempt + Random.Shared.Next(0, jitterMs))
                );
        }
    }

}
