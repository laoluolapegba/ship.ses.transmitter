//using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Serilog;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Worker;
using System.Linq;
var builder = Host.CreateApplicationBuilder(args);

// ✅ Configure Serilog with ElasticSearch & CorrelationId
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(
        new Uri(builder.Configuration["ElasticSearch:Uri"] ?? "http://localhost:9200"))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "logs-{0:yyyy.MM.dd}"
    })
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// ✅ Load Configuration (Supports appsettings.json & Environment Variables)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ✅ Register Services & Observability
builder.Services.AddAppServices(builder.Configuration);
builder.Services.ConfigureTracing(builder.Configuration);


// ✅ Register Background Workers for Each FHIR Resource Type
builder.Services.AddHostedService(sp =>
    new SyncWorker(sp.GetRequiredService<IServiceProvider>(),
                       sp.GetRequiredService<ILogger<SyncWorker>>(),
                       FhirResourceType.Patient));

builder.Services.AddHostedService(sp =>
    new SyncWorker(sp.GetRequiredService<IServiceProvider>(),
                       sp.GetRequiredService<ILogger<SyncWorker>>(),
                       FhirResourceType.Encounter));

var app = builder.Build();

// ✅ Ensure logs are flushed before shutdown
//app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);


app.Run();

