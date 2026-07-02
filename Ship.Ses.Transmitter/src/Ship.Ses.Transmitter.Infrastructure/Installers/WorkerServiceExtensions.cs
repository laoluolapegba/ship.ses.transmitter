using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain;
using Ship.Ses.Transmitter.Infrastructure.ReadServices;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using MySql.Data.MySqlClient;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Sync;
using static Org.BouncyCastle.Math.EC.ECCurve;
using System.Data;
using Ship.Ses.Transmitter.Worker;
using Polly;
using Polly.Extensions.Http;
using Ship.Ses.Transmitter.Infrastructure.Services;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Sync;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Infrastructure.Security;

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class ServiceExtensions
    {
        private static readonly TextMapPropagator Propagator = new TraceContextPropagator();
        public static void AddAppServices(this IServiceCollection services, IConfiguration configuration)
        {
            //  Bind DatabaseSettings from appsettings.json
            services.Configure<SourceDbSettings>(configuration.GetSection("SourceDbSettings"));

            //  Register MongoDB Client
            services.AddSingleton<IMongoClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return new MongoClient(settings.ConnectionString);
            });

            //  Register MongoDB Database
            services.AddScoped(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return client.GetDatabase(settings.DatabaseName);
            });

            //  Register Repositories & Services
            services.AddScoped<IFhirSyncStore, MongoSyncRepository>();
            services.AddScoped<IFhirSyncService, FhirSyncService>();

            //services.AddScoped<ISyncMetricsCollector, ClientSyncMetricsCollector>();
           // services.AddScoped<ISyncMetricsWriter, MySqlSyncMetricsWriter>();
            var appSettings = configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();

            if (appSettings != null)
            {
                var msSqlSettings = appSettings.ShipServerSqlDb;
            }
            // Outbound FHIR authorization defaults. Scope lives here (not per FHIR route): the same
            // credential/scope authenticates to every SHIP target system. Fail fast if misconfigured.
            services.AddOptions<AuthSettings>()
                .Bind(configuration.GetSection("AuthSettings"))
                .Validate(a => !string.IsNullOrWhiteSpace(a.TokenEndpoint), "AuthSettings:TokenEndpoint is required")
                .Validate(a => !string.IsNullOrWhiteSpace(a.Scope),
                    "AuthSettings:Scope is required (outbound authorization scope is no longer set per FHIR route)")
                .ValidateOnStart();
            services.AddSingleton<AdminTokenService>();

            // Multi-client outbound auth: credential resolved per clientId, token cached per (clientId, scope).
            services.AddHttpClient("FhirTokens");
            services.AddSingleton<IFhirTokenService, CachedFhirTokenService>();

            // Per-client outbound credentials come from configuration (AppSettings:Clients / AppSettings:Hmac).
            // Secret VALUES are injected into the runtime as environment variables before startup by the
            // organization's ISW secret-injection mechanism (a HashiCorp Vault agent) and bound over the
            // committed placeholders — e.g. AppSettings__Clients__0__ClientSecret. The application makes no
            // Vault API calls, knows no Vault address/token, and handles no X-Vault-Token. Clients are loaded
            // once at startup (no per-request lookups, no TTL); adding/rotating a client requires a restart.
            services.Configure<ClientDirectoryOptions>(configuration.GetSection("AppSettings"));
            services.AddSingleton<IClientCredentialProvider, ConfigClientCredentialProvider>();

            var configuredClients = configuration.GetSection("AppSettings:Clients").Get<List<ClientCredentialEntry>>() ?? new();
            var activeCount = configuredClients.Count(c => c is not null && c.IsActive);
            Console.WriteLine($"ClientCredentials: config provider (AppSettings:Clients), {activeCount} ACTIVE of {configuredClients.Count} configured. Secrets are ISW-injected via environment variables.");

            services.Configure<SeSClientOptions>(configuration.GetSection("SeSClient"));

            var sesSetting = configuration.GetSection("SeSClient");
            Console.WriteLine($"  TenantId: {sesSetting["TenantId"] ?? sesSetting["ClientId"]}");

            static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
                HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1));
          

        }
        public static void ConfigureLogging(this WebApplicationBuilder builder)
        {
            var elasticUri = builder.Configuration["ElasticSearch:Uri"] ?? "http://localhost:9200";

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithCorrelationId()
                .WriteTo.Console()
                .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUri))
                {
                    AutoRegisterTemplate = true,
                    IndexFormat = "logs-{0:yyyy.MM.dd}",
                    NumberOfReplicas = 1,
                    NumberOfShards = 2
                })
                .CreateLogger();

            builder.Logging.AddSerilog();
        }
        public static void ConfigureTracing(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("FHIRSyncService"))
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        //.AddMongoDBInstrumentation()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                        });
                });

            services.AddSingleton(Propagator);
        }
        public static IServiceCollection AddFhirApiClient(this IServiceCollection services, IConfiguration config)
        {
            services.AddHttpClient("FhirApi", (sp, client) =>
            {
                var routing = sp.GetRequiredService<IOptionsMonitor<FhirRoutingSettings>>().CurrentValue;
                var defaults = routing.Default ?? new FhirRouteSettings();

                if (!string.IsNullOrWhiteSpace(defaults.BaseUrl))
                {
                    client.BaseAddress = new Uri(defaults.BaseUrl);
                }

                var timeout = defaults.TimeoutSeconds > 0 ? defaults.TimeoutSeconds : 30;
                client.Timeout = TimeSpan.FromSeconds(timeout);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddSingleton<IFhirApiService, FhirApiService>();

            return services;
        }

        public static IServiceCollection AddSyncMetrics(this IServiceCollection services, IConfiguration config)
        {
            
            services.AddScoped<ISyncMetricsCollector, ClientSyncMetricsCollector>();
            
            return services;
        }
        
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"⚠️ Retry {retryAttempt} after {timespan.TotalSeconds} seconds due to {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromMinutes(1),
                    onBreak: (result, breakDelay) =>
                    {
                        Console.WriteLine($"🚨 Circuit broken! Breaking for {breakDelay.TotalSeconds} seconds due to {result.Exception?.Message ?? result.Result?.StatusCode.ToString()}");
                    },
                    onReset: () => Console.WriteLine("🔁 Circuit reset."),
                    onHalfOpen: () => Console.WriteLine("🕵️ Circuit in test mode (half-open)."));
        }
    }

}
