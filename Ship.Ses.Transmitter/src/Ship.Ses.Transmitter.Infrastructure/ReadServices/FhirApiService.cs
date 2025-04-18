using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Domain.SyncModels;

using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Services
{
    public class FhirApiService : IFhirApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FhirApiService> _logger;
        private readonly FhirApiSettings _settings;

        public FhirApiService(
            IHttpClientFactory httpClientFactory,
            IOptions<FhirApiSettings> settings,
            ILogger<FhirApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<HttpResponseMessage> SendAsync(
            FhirOperation operation,
            string resourceType,
            string resourceId = null,
            string jsonPayload = null,
            CancellationToken cancellationToken = default)
        {
            var client = _httpClientFactory.CreateClient("FhirApi");

            var method = operation switch
            {
                FhirOperation.Get => HttpMethod.Get,
                FhirOperation.Post => HttpMethod.Post,
                FhirOperation.Put => HttpMethod.Put,
                FhirOperation.Delete => HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), "Unsupported FHIR operation")
            };

            string endpoint = operation switch
            {
                FhirOperation.Post => $"{_settings.BaseUrl}/api/v1/{resourceType}/Create",
                FhirOperation.Put => $"{_settings.BaseUrl}/api/v1/{resourceType}/Update/{resourceId}",
                FhirOperation.Delete => $"{_settings.BaseUrl}/api/v1/{resourceType}/Delete/{resourceId}",
                FhirOperation.Get => $"{_settings.BaseUrl}/api/v1/{resourceType}/Get/{resourceId}",
                _ => throw new InvalidOperationException("Unknown FHIR operation")
            };

            var request = new HttpRequestMessage(method, endpoint);

            if (!string.IsNullOrEmpty(jsonPayload) && (method == HttpMethod.Post || method == HttpMethod.Put))
            {
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            }

            _logger.LogInformation("📡 Sending {Method} request to {Endpoint} for {ResourceType} (id={ResourceId})",
                method, endpoint, resourceType, resourceId ?? "<new>");

            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var deserialized = JsonSerializer.Deserialize<FhirApiResponse>(responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("⚠️ {StatusCode} - {Message}", response.StatusCode, deserialized?.Message);

                    if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity ||
                        response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogDebug("❗ Error details: {@Error}", deserialized);
                    }

                    response.EnsureSuccessStatusCode(); // optional: rethrow
                }

                _logger.LogInformation("✅ {ResourceType}/{Operation} completed: {Code} - {Message}",
                    resourceType, operation, deserialized?.Code, deserialized?.Message);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during FHIR API call for {ResourceType} [{Operation}]", resourceType, operation);
                throw;
            }
        }
    }
}