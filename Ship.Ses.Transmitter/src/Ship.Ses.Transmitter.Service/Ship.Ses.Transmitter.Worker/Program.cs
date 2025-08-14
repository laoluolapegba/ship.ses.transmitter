//using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Worker;
using static Org.BouncyCastle.Math.EC.ECCurve;

var builder = Host.CreateApplicationBuilder(args);
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

builder.Logging.ClearProviders();
//  Configure Serilog with ElasticSearch & CorrelationId
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()    
    .CreateLogger();
builder.Logging.AddSerilog();


builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services
    .AddOptions<FhirApiSettings>()
    .Bind(builder.Configuration.GetSection("FhirApi"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "FhirApi:BaseUrl is required")
    .Validate(o => o.TimeoutSeconds > 0, "FhirApi:TimeoutSeconds must be > 0")
    .ValidateOnStart();

//builder.Services.ConfigureTracing(builder.Configuration);



var appSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>();
if (appSettings != null)
{

    var msSqlSettings = appSettings.ShipServerSqlDb;
    builder.Services.AddDbContext<ShipServerDbContext>(options =>
    {
        options.UseMySQL(msSqlSettings.ConnectionString);
    });

    builder.Services.AddPooledDbContextFactory<ExtractorStagingDbContext>(opts =>
    {
        //var cs = builder.Configuration.GetConnectionString(msSqlSettings.ConnectionString);
        opts.UseMySQL(msSqlSettings.ConnectionString);
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

//  Register Background Workers for Each FHIR Resource Type


builder.Services.AddHostedService<PatientSyncWorker>();
builder.Services.AddHostedService<EncounterSyncWorker>();
builder.Services.AddScoped<IStagingUpdateWriter, StagingUpdateWriter>();

//builder.Services.AddHostedService<MetricsSyncReporterWorker>();


//var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
//Console.WriteLine(test == null
//    ? "❌ ISyncMetricsCollector not registered"
//    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();


app.Run();

