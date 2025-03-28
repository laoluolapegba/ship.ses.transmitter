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

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class ServiceExtensions
    {
        private static readonly TextMapPropagator Propagator = new TraceContextPropagator();
        public static void AddAppServices(this IServiceCollection services, IConfiguration configuration)
        {
            // ✅ Bind DatabaseSettings from appsettings.json
            services.Configure<SourceDbSettings>(configuration.GetSection("SourceDbSettings"));

            // ✅ Register MongoDB Client
            services.AddSingleton<IMongoClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return new MongoClient(settings.ConnectionString);
            });

            // ✅ Register MongoDB Database
            services.AddScoped(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return client.GetDatabase(settings.DatabaseName);
            });

            // ✅ Register Repositories & Services
            services.AddScoped<IFhirSyncRepositoryFactory, FhirSyncRepositoryFactory>();
            services.AddScoped<IFhirSyncRepository, FhirSyncRepository>();
            services.AddScoped<IFhirSyncService, FhirSyncService>();

            services.AddScoped<ISyncMetricsCollector, ClientSyncMetricsCollector>();
            services.AddScoped<ISyncMetricsWriter, MySqlSyncMetricsWriter>();
            var appSettings = configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();

            if (appSettings != null)
            {
                var msSqlSettings = appSettings.ShipServerSqlDb;
            }
            services.Configure<SyncClientOptions>(configuration.GetSection("SeSClient"));

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

        public static IServiceCollection AddSyncMetrics(this IServiceCollection services, IConfiguration config)
        {

            // ✅ Bind DatabaseSettings from appsettings.json
            services.Configure<SourceDbSettings>(config.GetSection("SourceDbSettings"));

            // ✅ Register MongoDB Client
            services.AddSingleton<IMongoClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return new MongoClient(settings.ConnectionString);
            });

            // ✅ Register MongoDB Database
            services.AddScoped(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                var settings = sp.GetRequiredService<IOptions<SourceDbSettings>>().Value;
                return client.GetDatabase(settings.DatabaseName);
            });

            // ✅ Register Repositories & Services
            services.AddScoped<IFhirSyncRepositoryFactory, FhirSyncRepositoryFactory>();
            services.AddScoped<IFhirSyncRepository, FhirSyncRepository>();
            services.AddScoped<IFhirSyncService, FhirSyncService>();

            services.AddScoped<ISyncMetricsCollector, ClientSyncMetricsCollector>();
            services.AddScoped<ISyncMetricsWriter, MySqlSyncMetricsWriter>();
            var appSettings = config.GetSection(nameof(AppSettings)).Get<AppSettings>();

            if (appSettings != null)
            {
                var msSqlSettings = appSettings.ShipServerSqlDb;
            }
            services.Configure<SyncClientOptions>(config.GetSection("SeSClient"));
            services.Configure<SyncOptions>(config.GetSection("ResourceSync"));

            
            if (appSettings != null)
            {
                var msSqlSettings = appSettings.ShipServerSqlDb;
                services.AddDbContext<AppDbContext>(options =>
                 options.UseMySQL(msSqlSettings.ConnectionString));
                services.AddScoped<IAppDbContext>(provider => provider.GetService<AppDbContext>());
            }

            return services;
        }
        public static IServiceCollection AddFhirApiClient(this IServiceCollection services, IConfiguration config)
        {
            services.Configure<FhirApiSettings>(config.GetSection("FhirApi"));

            services.AddHttpClient("FhirApi", (sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<FhirApiSettings>>().Value;
                client.BaseAddress = new Uri(settings.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<FhirApiSettings>>().Value;
                var certPath = Path.Combine(AppContext.BaseDirectory, settings.ClientCertPath);
                var cert = new X509Certificate2(certPath, settings.ClientCertPassword);

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                handler.ClientCertificates.Add(cert);
                return handler;
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddScoped<IFhirApiService, FhirApiService>();

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
