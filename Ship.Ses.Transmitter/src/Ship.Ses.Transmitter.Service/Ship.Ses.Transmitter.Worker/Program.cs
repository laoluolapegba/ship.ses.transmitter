//using Google.Protobuf.WellKnownTypes;
using Serilog;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Worker;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using static K4os.Compression.LZ4.Engine.Pubternal;
using Polly;
using Polly.Extensions.Http;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using static Org.BouncyCastle.Math.EC.ECCurve;

var builder = Host.CreateApplicationBuilder(args);
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

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



var appSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();
if (appSettings != null)
{

    var msSqlSettings = appSettings.ShipServerSqlDb;
    builder.Services.AddDbContext<ShipServerDbContext>(options =>
    {
        options.UseMySQL(msSqlSettings.ConnectionString);
    });
}
else
{
    throw new Exception("AppSettings not found");
}
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(nameof(AppSettings)));



builder.Services.AddAppServices(builder.Configuration);

builder.Services
    .AddFhirApiClient(builder.Configuration)
    .AddSyncMetrics(builder.Configuration);

// ✅ Register Background Workers for Each FHIR Resource Type


builder.Services.AddHostedService<PatientSyncWorker>();
builder.Services.AddHostedService<EncounterSyncWorker>();
//builder.Services.AddHostedService<MetricsSyncReporterWorker>();


var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
Console.WriteLine(test == null
    ? "❌ ISyncMetricsCollector not registered"
    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();


app.Run();

