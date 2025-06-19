using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Ship.Ses.Transmitter.Domain.Patients;
    using Ship.Ses.Transmitter.Infrastructure.Settings;

    public class TokenService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthSettings _authSettings;
        private readonly ILogger<TokenService> _logger;

        public TokenService(HttpClient httpClient, IOptions<AuthSettings> authOptions, ILogger<TokenService> logger)
        {
            _httpClient = httpClient;
            _authSettings = authOptions.Value;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            var payload = new
            {
                clientId = _authSettings.ClientId,
                clientSecret = _authSettings.ClientSecret,
                grantType = _authSettings.GrantType,
                scope = _authSettings.Scope
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, _authSettings.TokenEndpoint)
            {
                Content = content
            };

            _logger.LogInformation("🔐 Requesting access token...");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<AuthTokenResponse>(json);

            if (tokenResponse?.Data?.AccessToken == null)
            {
                _logger.LogError("❌ Token response did not include access token: {Json}", json);
                throw new InvalidOperationException("Token endpoint did not return a valid access token.");
            }

            _logger.LogInformation("✅ Token acquired, expires in {ExpiresIn} seconds", tokenResponse.Data.ExpiresIn);
            return tokenResponse.Data.AccessToken;
        }
    }

}
