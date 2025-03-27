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

            // services.AddScoped<IDbConnection>(sp => new MySqlConnection(
            //    configuration.GetConnectionString("MonitoringDb")));


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
    }
}
