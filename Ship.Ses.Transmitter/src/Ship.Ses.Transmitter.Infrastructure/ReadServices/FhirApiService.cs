using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Domain.SyncModels;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Services
{
    public class FhirApiService : IFhirApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FhirApiService> _logger;
        private readonly FhirApiSettings _settings;
        private readonly TokenService _tokenService;
        public FhirApiService(
            IHttpClientFactory httpClientFactory,
            IOptions<FhirApiSettings> settings,
            IOptions<AuthSettings> authSettings,
            ILogger<FhirApiService> logger,
            TokenService tokenService)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
            _tokenService = tokenService;
        }


        public async Task<FhirApiResponse> SendAsync(
        FhirOperation operation,
        string resourceType,
        string resourceId = null,
        string jsonPayload = null,
        string? callbackUrl = null,
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
                using var doc = JsonDocument.Parse(jsonPayload);

                var envelope = new FhirRequestEnvelope
                {
                    CallbackUrl = callbackUrl,
                    Data = doc.RootElement.Clone()
                };

                var wrappedJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                request.Content = new StringContent(wrappedJson, Encoding.UTF8, "application/json");
                _logger.LogInformation("📦 Wrapped FHIR payload: {Payload}", wrappedJson);
            }

            _logger.LogInformation("📡 Sending {Method} request to {Endpoint} for {ResourceType} (id={ResourceId})",
                method, endpoint, resourceType, resourceId ?? "<new>");

            try
            {
                var token = await _tokenService.GetAccessTokenAsync(cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("📬 Response Body: {ResponseBody}", responseBody);

                var deserialized = JsonSerializer.Deserialize<FhirApiResponse>(responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("⚠️ {StatusCode} - {Message}", response.StatusCode, deserialized?.Message);
                    _logger.LogDebug("❗ Error details: {@Error}", deserialized);

                    response.EnsureSuccessStatusCode(); // will still throw
                }

                _logger.LogInformation("✅ {ResourceType}/{Operation} completed: {Code} - {Message}",
                    resourceType, operation, deserialized?.Code, deserialized?.Message);

                return deserialized;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during FHIR API call for {ResourceType} [{Operation}]", resourceType, operation);
                throw;
            }
        }



    }

}