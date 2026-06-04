using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
using Ship.Ses.Transmitter.Infrastructure.Security;
using Ship.Ses.Transmitter.Infrastructure.Settings;

namespace Ship.Ses.Transmitter.Infrastructure.Services
{
    public class FhirApiService : IFhirApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FhirApiService> _logger;
        private readonly IOptionsMonitor<FhirRoutingSettings> _routingSettings;
        private readonly IOptions<AuthSettings> _authSettings;
        private readonly IFhirTokenService _fhirTokenService;
        public FhirApiService(
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<FhirRoutingSettings> routingSettings,
            IOptions<AuthSettings> authSettings,
            ILogger<FhirApiService> logger,
            IFhirTokenService fhirTokenService)
        {
            _httpClientFactory = httpClientFactory;
            _routingSettings = routingSettings;
            _authSettings = authSettings;
            _logger = logger;
            _fhirTokenService = fhirTokenService;
        }


        public async Task<FhirApiResponse> SendAsync(
    FhirOperation operation,
    string clientId,
    string resourceType,
    string resourceId = null,
    string jsonPayload = null,
    string? callbackUrl = null,
    string? shipService = null,
    CancellationToken cancellationToken = default)
        {
            var (routeName, route) = _routingSettings.CurrentValue.ResolveRoute(shipService, resourceType);
            if (string.IsNullOrWhiteSpace(route.BaseUrl))
                throw new InvalidOperationException($"FHIR route '{routeName}' does not have a BaseUrl configured.");

            var client = _httpClientFactory.CreateClient("FhirApi");
            var method = operation switch
            {
                FhirOperation.Get => HttpMethod.Get,
                FhirOperation.Post => HttpMethod.Post,
                FhirOperation.Put => HttpMethod.Put,
                FhirOperation.Delete => HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), "Unsupported FHIR operation")
            };

            var baseUrl = route.BaseUrl.TrimEnd('/');
            callbackUrl ??= route.CallbackUrlTemplate ?? _routingSettings.CurrentValue.Default?.CallbackUrlTemplate;

            // The callback URL is the Ingestor ack endpoint SHIP calls back on completion. If it is blank,
            // SHIP has nowhere to post the result and the record can only be resolved by the probe fallback —
            // make that explicit in the logs instead of failing silently.
            if (string.IsNullOrWhiteSpace(callbackUrl))
                _logger.LogWarning("⚠️ No CallbackUrl resolved for {ResourceType} via route {Route} (client {ClientId}). " +
                    "SHIP cannot ack this send; status will depend on the StatusProbe fallback. " +
                    "Set FhirRouting:Default:CallbackUrlTemplate to the Ingestor ack URL (e.g. http://<host>/api/v1/patient/ack).",
                    resourceType, routeName, clientId);

            // detect bundle
            var normalized = TryExtractResourceType(jsonPayload) ?? resourceType;

            string endpoint;
            if (string.Equals(shipService, "PDS", StringComparison.OrdinalIgnoreCase))
            {

                if (string.Equals(normalized, "Bundle", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = "/api/v1/Bundle";
                    endpoint = $"{baseUrl}/{relativePath.TrimStart('/')}";
                    method = HttpMethod.Post;
                }
                else
                {
                    endpoint = operation switch
                    {
                        FhirOperation.Post => $"{baseUrl}/api/v1/{normalized}",
                        FhirOperation.Put => $"{baseUrl}/api/v1/{normalized}/{resourceId}",
                        FhirOperation.Delete => $"{baseUrl}/api/v1/{normalized}/{resourceId}",
                        FhirOperation.Get => $"{baseUrl}/api/v1/{normalized}/{resourceId}",
                        _ => throw new InvalidOperationException("Unknown FHIR operation")
                    };
                }
            }
            else
            {
                endpoint = operation switch
                {
                    FhirOperation.Post => $"{baseUrl}",
                    _ => throw new NotSupportedException($"Operation {operation} not supported for service {shipService}")
                };
            }

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
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                request.Content = new StringContent(wrappedJson, Encoding.UTF8, "application/json");
                _logger.LogInformation("📦 Wrapped FHIR payload: {Payload}", Trunc(wrappedJson, 1000));
            }

            _logger.LogInformation("📡 Sending {Method} {Endpoint} for {ResourceType} (id={ResourceId}) via {Route} for client {ClientId}. CallbackUrl={CallbackUrl}",
                method, endpoint, resourceType, resourceId ?? "<new>", routeName, clientId, callbackUrl ?? "<none>");

            try
            {
                // Credential is resolved per clientId; scope comes from AuthSettings (authorization is the
                // same across every SHIP target system — never route/shipService-specific).
                var scope = _authSettings.Value.Scope;
                var token = await _fhirTokenService.GetAccessTokenAsync(clientId, scope, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (route.TimeoutSeconds > 0) linkedCts.CancelAfter(TimeSpan.FromSeconds(route.TimeoutSeconds));

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                var mediaType = response.Content?.Headers?.ContentType?.MediaType;
                var responseRaw = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("📬 SHIP FHIR replied HTTP {Status}. Content-Type={CT}. Body: {Body}",
                    (int)response.StatusCode, mediaType ?? "(none)", Trunc(responseRaw, 1000));

                FhirApiResponse? parsed = null;
                if (!string.IsNullOrWhiteSpace(responseRaw) && LooksLikeJson(mediaType, responseRaw))
                {
                    try
                    {
                        parsed = JsonSerializer.Deserialize<FhirApiResponse>(
                            responseRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsed != null) parsed.Raw = responseRaw;
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogWarning(jex, "⚠️ Failed to parse JSON response. Body (truncated): {Body}", Trunc(responseRaw, 1000));
                    }
                }

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
                        transactionId = parsed?.transactionId,
                        Data = parsed?.Data,
                        Raw = responseRaw
                    };
                    _logger.LogWarning("⚠️ {StatusCode} - {Message}", response.StatusCode, error.Message);
                    return error;
                }

                if (parsed is null)
                {
                    parsed = new FhirApiResponse
                    {
                        Status = "success",
                        Code = (int)response.StatusCode,
                        Message = string.IsNullOrWhiteSpace(responseRaw) ? "No body" : "OK",
                        Raw = responseRaw
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
        }

        // Helper(s)
        private static string? TryExtractResourceType(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("resourceType", out var rt) && rt.ValueKind == JsonValueKind.String)
                    return rt.GetString();
            }
            catch { }
            return null;
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


    }

}
