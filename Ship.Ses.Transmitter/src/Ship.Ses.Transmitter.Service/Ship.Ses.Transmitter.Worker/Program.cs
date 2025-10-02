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
    .AddOptions<FhirRoutingSettings>()
    .Bind(builder.Configuration.GetSection("FhirRouting"))
    .Validate(o => o?.Default is not null && !string.IsNullOrWhiteSpace(o.Default.BaseUrl), "FhirRouting:Default:BaseUrl is required")
    .Validate(o => o?.Default?.TimeoutSeconds > 0, "FhirRouting:Default:TimeoutSeconds must be > 0")
    .Validate(o => !string.IsNullOrWhiteSpace(o?.CallbackUrlTemplate), "FhirRouting:CallbackUrlTemplate is required")
    .ValidateOnStart();

builder.Services.AddHttpClient("EmrCallback")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(15);
    });


builder.Services
    .AddOptions<AppSettings>()
    .Bind(builder.Configuration.GetSection("AppSettings"))
    .ValidateDataAnnotations()
    .Validate(o =>
        o.ShipServerSqlDb is not null &&
        !string.IsNullOrWhiteSpace(o.ShipServerSqlDb.DbType) &&
        o.EmrDb is not null &&
        !string.IsNullOrWhiteSpace(o.EmrDb.DbType),
        "Both ShipServerSqlDb and EmrDb must be configured")
    .ValidateOnStart();
static void UseProviderWithSchema(DbContextOptionsBuilder opts, DatabaseSettings db)
{
    var kind = db.DbType.Trim().ToLowerInvariant();
    var schema = string.IsNullOrWhiteSpace(db.Schema) ? null : db.Schema;

    switch (kind)
    {
        case "postgres":
        case "postgresql":
            var cs = schema is null ? db.ConnectionString : $"{db.ConnectionString};Search Path={schema}";
            opts.UseNpgsql(cs);
            break;

        case "sqlserver":
            opts.UseSqlServer(db.ConnectionString);
            break;

        case "mysql":
            opts.UseMySQL(db.ConnectionString);
            break;

        default:
            throw new InvalidOperationException($"Unsupported DbType: {db.DbType}");
    }
}

builder.Services.AddDbContext<ShipServerDbContext>(opts =>
{
    var app = builder.Configuration.GetSection("AppSettings").Get<AppSettings>()!;
    UseProviderWithSchema(opts, app.ShipServerSqlDb);
});

builder.Services.AddPooledDbContextFactory<ExtractorStagingDbContext>(opts =>
{
    var app = builder.Configuration.GetSection("AppSettings").Get<AppSettings>()!;
    UseProviderWithSchema(opts, app.EmrDb);
});


builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(nameof(AppSettings)));




builder.Services.AddAppServices(builder.Configuration);

builder.Services
    .AddFhirApiClient(builder.Configuration)
    .AddSyncMetrics(builder.Configuration);

//  Register Background Workers for Each FHIR Resource Type

builder.Services.AddHostedService<PatientSyncWorker>();
//builder.Services.AddHostedService<EncounterSyncWorker>();

//Register this guy to report/ update the emr staging db 
builder.Services.AddScoped<IStagingUpdateWriter, StagingUpdateWriter>();

builder.Services.AddHostedService<EmrCallbackWorker>();

//var test = builder.Services.BuildServiceProvider().GetService<ISyncMetricsCollector>();
//Console.WriteLine(test == null
//    ? "❌ ISyncMetricsCollector not registered"
//    : "✅ ISyncMetricsCollector is registered");

var app = builder.Build();


app.Run();

