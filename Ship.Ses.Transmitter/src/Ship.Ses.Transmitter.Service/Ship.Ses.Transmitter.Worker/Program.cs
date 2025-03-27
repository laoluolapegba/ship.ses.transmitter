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
using static K4os.Compression.LZ4.Engine.Pubternal;
var builder = Host.CreateApplicationBuilder(args);
//builder.InstallAppDbContext();
builder.Logging.ClearProviders();
// ✅ Configure Serilog with ElasticSearch & CorrelationId
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    //.WriteTo.Console()
    //.WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(
        new Uri(builder.Configuration["ElasticSearch:Uri"] ?? "http://localhost:9200"))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "logs-{0:yyyy.MM.dd}"
    })
    .CreateLogger();

//builder.Host.UseSerilog();

builder.Logging.AddSerilog();

// ✅ Load Configuration (Supports appsettings.json & Environment Variables)
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ✅ Register Services & Observability
builder.Services.AddAppServices(builder.Configuration);
builder.Services.ConfigureTracing(builder.Configuration);
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("ResourceSync"));

var appSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();
if (appSettings != null)
{
    var msSqlSettings = appSettings.ShipServerSqlDb;
    builder.Services.AddDbContext<AppDbContext>(options =>
     options.UseMySQL(msSqlSettings.ConnectionString));
    builder.Services.AddScoped<IAppDbContext>(provider => provider.GetService<AppDbContext>());
}

// ✅ Register Background Workers for Each FHIR Resource Type

var syncOptions = builder.Configuration.GetSection("ResourceSync").Get<SyncOptions>();

if (syncOptions.Patient.Enabled)
    builder.Services.AddHostedService<PatientSyncWorker>();

if (syncOptions.Patient.Enabled)
    builder.Services.AddHostedService<EncounterSyncWorker>();

builder.Services.AddHostedService<MetricsSyncReporterWorker>();


//if (syncOptions.Encounter.Enabled)
//    builder.Services.AddHostedService<EncounterSyncWorker>();
//builder.Services.AddHostedService(sp =>
//    new SyncWorker(sp.GetRequiredService<IServiceProvider>(),
//                       sp.GetRequiredService<ILogger<SyncWorker>>(),
//                       FhirResourceType.Encounter));


var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
Console.WriteLine(test == null
    ? "❌ ISyncMetricsCollector not registered"
    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();

//using (var scope = app.Services.CreateScope())
//{
//    var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    //EntityFrameworkInstaller.SeedDatabase(appDbContext);
//}
;
// ✅ Ensure logs are flushed before shutdown
//app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);


app.Run();

