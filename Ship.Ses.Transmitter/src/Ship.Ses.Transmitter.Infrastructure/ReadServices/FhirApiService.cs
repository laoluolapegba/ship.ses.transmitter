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
using System.Linq;
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
        private readonly IOptionsMonitor<FhirRoutingSettings> _routingSettings;
        private readonly string? _defaultScope;
        private readonly TokenService _tokenService;
        public FhirApiService(
            IHttpClientFactory httpClientFactory,
            IOptions<FhirRoutingSettings> routingSettings,
            IOptions<AuthSettings> authSettings,
            ILogger<FhirApiService> logger,
            TokenService tokenService)
        {
            _httpClientFactory = httpClientFactory;
            _routingSettings = routingSettings;
            _logger = logger;
            _tokenService = tokenService;
            _defaultScope = authSettings.Value.Scope;
        }


        public async Task<FhirApiResponse> SendAsync(
        FhirOperation operation,
        string resourceType,
        string resourceId = null,
        string jsonPayload = null,
        string? callbackUrl = null,
        string? shipService = null,
    CancellationToken cancellationToken = default)
        {
            var routing = _routingSettings.CurrentValue
                ?? throw new InvalidOperationException("Fhir routing configuration is missing.");

            var route = ResolveRoute(routing, shipService, resourceType);
            var (client, shouldDisposeClient) = CreateClient(route);

            var method = operation switch
            {
                FhirOperation.Get => HttpMethod.Get,
                FhirOperation.Post => HttpMethod.Post,
                FhirOperation.Put => HttpMethod.Put,
                FhirOperation.Delete => HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), "Unsupported FHIR operation")
            };

            string endpoint = BuildEndpoint(route, operation, resourceType, resourceId);

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
                var scope = string.IsNullOrWhiteSpace(route.Scope) ? _defaultScope : route.Scope;
                var token = await _tokenService.GetAccessTokenAsync(cancellationToken, scope);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                var mediaType = response.Content?.Headers?.ContentType?.MediaType;
                var responseRaw = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("📬 SHIP FHIR replied HTTP {Status}. Content-Type={CT}. Body: {Body}",
                    (int)response.StatusCode,
                    mediaType ?? "(none)",
                    Trunc(responseRaw, 1000));

                FhirApiResponse? parsed = null;

                // Only try to parse when there is a non-empty, JSON-looking payload
                if (!string.IsNullOrWhiteSpace(responseRaw) && LooksLikeJson(mediaType, responseRaw))
                {
                    try
                    {
                        parsed = JsonSerializer.Deserialize<FhirApiResponse>(
                            responseRaw,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogWarning(jex, "⚠️ Failed to parse JSON response. Body (truncated): {Body}", Trunc(responseRaw, 1000));
                    }
                }

                // If HTTP is non-success, return a structured error (don’t throw here)
                if (!response.IsSuccessStatusCode)
                {
                    var message = parsed?.Message;
                    if (string.IsNullOrWhiteSpace(message))
                        message = !string.IsNullOrWhiteSpace(responseRaw) ? Trunc(responseRaw, 200) : response.ReasonPhrase ?? "HTTP error";

                    var error = new FhirApiResponse
                    {
                        Status = parsed?.Status ?? "error",
                        Code = (int)response.StatusCode,
                        Message = message,
                        transactionId = parsed?.transactionId
                    };

                    _logger.LogWarning("⚠️ {StatusCode} - {Message}", response.StatusCode, error.Message);
                    _logger.LogDebug("❗ Error payload: {@Payload}", parsed ?? (object)responseRaw ?? "(empty)");
                    return error;
                }

                // Success path: if no JSON body, synthesize a minimal success response
                if (parsed is null)
                {
                    parsed = new FhirApiResponse
                    {
                        Status = "success",
                        Code = (int)response.StatusCode,
                        Message = string.IsNullOrWhiteSpace(responseRaw) ? "No body" : "OK"
                    };
                }

                _logger.LogInformation("✅ {ResourceType}/{Operation} completed: {Code} - {Message}",
                    resourceType, operation, parsed.Code, parsed.Message);

                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during FHIR API call for {ResourceType} [{Operation}]", resourceType, operation);
                throw;
            }
            finally
            {
                if (shouldDisposeClient)
                {
                    client.Dispose();
                }
            }
        }
        static bool LooksLikeJson(string? mediaType, string body)
        {
            if (!string.IsNullOrEmpty(mediaType) && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return true;
            var s = body.AsSpan().TrimStart();
            return s.Length > 0 && (s[0] == '{' || s[0] == '[');
        }

        static string Trunc(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var flat = s.Replace("\r", " ").Replace("\n", " ");
            return flat.Length <= max ? flat : flat[..max] + "…";
        }

        private static string BuildEndpoint(FhirApiRouteSettings route, FhirOperation operation, string resourceType, string? resourceId)
        {
            if (route?.BaseUrl is null)
                throw new InvalidOperationException($"Fhir route baseUrl is not configured for resource {resourceType}.");

            var trimmedBase = route.BaseUrl.TrimEnd('/');
            var path = operation switch
            {
                FhirOperation.Post => $"api/v1/{resourceType}",
                FhirOperation.Put => $"api/v1/{resourceType}/{resourceId}",
                FhirOperation.Delete => $"api/v1/{resourceType}/{resourceId}",
                FhirOperation.Get => $"api/v1/{resourceType}/{resourceId}",
                _ => throw new InvalidOperationException("Unknown FHIR operation")
            };

            return $"{trimmedBase}/{path}";
        }

        private (HttpClient Client, bool ShouldDispose) CreateClient(FhirApiRouteSettings route)
        {
            if (route?.ClientCert?.HasValue == true)
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                var certPath = route.ClientCert.Path!;
                if (!Path.IsPathRooted(certPath))
                {
                    certPath = Path.Combine(AppContext.BaseDirectory, certPath);
                }

                if (!File.Exists(certPath))
                {
                    throw new FileNotFoundException($"Client certificate not found at path '{route.ClientCert.Path}'.", certPath);
                }

                handler.ClientCertificates.Add(new X509Certificate2(certPath, route.ClientCert.Password));

                var client = new HttpClient(handler, disposeHandler: true);
                if (route.TimeoutSeconds > 0)
                {
                    client.Timeout = TimeSpan.FromSeconds(route.TimeoutSeconds);
                }
                return (client, true);
            }

            var sharedClient = _httpClientFactory.CreateClient("FhirApi");
            if (route?.TimeoutSeconds > 0)
            {
                sharedClient.Timeout = TimeSpan.FromSeconds(route.TimeoutSeconds);
            }

            return (sharedClient, false);
        }

        private static FhirApiRouteSettings ResolveRoute(FhirRoutingSettings routing, string? shipService, string resourceType)
        {
            if (!string.IsNullOrWhiteSpace(shipService) && routing.Apis is not null)
            {
                var match = routing.Apis.FirstOrDefault(api =>
                    !string.IsNullOrWhiteSpace(api.Name) &&
                    shipService.Equals(api.Name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match;
            }

            if (routing.Apis is not null)
            {
                var byResource = routing.Apis.FirstOrDefault(api =>
                    api.Resources != null && api.Resources.Contains(resourceType, StringComparer.OrdinalIgnoreCase));
                if (byResource is not null)
                    return byResource;
            }

            if (routing.Default is not null)
                return routing.Default;

            throw new InvalidOperationException($"No FHIR API route configured for resource '{resourceType}'.");
        }


    }

}