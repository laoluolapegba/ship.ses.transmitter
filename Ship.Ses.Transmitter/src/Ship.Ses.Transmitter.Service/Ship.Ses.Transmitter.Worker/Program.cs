//using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Serilog;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Sync;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Worker;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using static K4os.Compression.LZ4.Engine.Pubternal;
using Polly;
using Polly.Extensions.Http;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
// ✅ Configure Serilog with ElasticSearch & CorrelationId
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(
        new Uri(builder.Configuration["ElasticSearch:Uri"] ?? "http://localhost:9200"))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "logs-{0:yyyy.MM.dd}"
    })
    .CreateLogger();
builder.Logging.AddSerilog();

// ✅ Load Configuration (Supports appsettings.json & Environment Variables)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ✅ Register Services & Observability
builder.Services.ConfigureTracing(builder.Configuration);

builder.Services
    .AddFhirApiClient(builder.Configuration)
    .AddSyncMetrics(builder.Configuration);

// ✅ Register Background Workers for Each FHIR Resource Type

var syncOptions = builder.Configuration.GetSection("ResourceSync").Get<SyncOptions>();

if (syncOptions.Patient.Enabled)
    builder.Services.AddHostedService<PatientSyncWorker>();

if (syncOptions.Patient.Enabled)
    builder.Services.AddHostedService<EncounterSyncWorker>();

builder.Services.AddHostedService<MetricsSyncReporterWorker>();


var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
Console.WriteLine(test == null
    ? "❌ ISyncMetricsCollector not registered"
    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();


app.Run();

