using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.UnitTests.Fakes;

/// <summary>
/// Test <see cref="HttpMessageHandler"/> that counts requests and returns a canned response.
/// Optionally delays so concurrency (single-flight) can be exercised.
/// </summary>
public sealed class CountingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private int _count;

    public CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public int CallCount => Volatile.Read(ref _count);

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);
        return _responder(request);
    }

    /// <summary>Builds the SHIP-shaped success token envelope used by the token services.</summary>
    public static HttpResponseMessage TokenResponse(string accessToken, int expiresIn) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $"{{\"status\":\"success\",\"code\":200,\"message\":\"ok\",\"data\":{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn},\"scope\":\"ship\"}}}}",
            Encoding.UTF8, "application/json")
    };
}
