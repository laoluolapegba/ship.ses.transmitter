﻿using Ship.Ses.Transmitter.Infrastructure.Installers;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MsSql;
using Scalar.AspNetCore;
using StackExchange.Redis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Ship.Ses.Transmitter.WebApi.Installers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Ship.Ses.Transmitter.Application.Patients;
using Ship.Ses.Transmitter.Infrastructure.Persistance;
using MongoDB.Driver;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain;
using Ship.Ses.Transmitter.Infrastructure.Persistance.Configuration.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Ship.Ses.Transmitter.Infrastructure.Persistance.MySql;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddOktaAuthentication(builder.Configuration);

// Configure SourceDbSettings from appsettings.json
builder.Services.Configure<SourceDbSettings>(builder.Configuration.GetSection("SourceDbSettings"));

// Register IMongoClient as a Singleton
builder.Services.AddSingleton<IMongoClient>(s =>
{
    // Get SourceDbSettings via IOptions<SourceDbSettings>
    var settings = s.GetRequiredService<IOptions<SourceDbSettings>>().Value; 

    if (string.IsNullOrEmpty(settings.ConnectionString)) 
    {
        throw new InvalidOperationException("SourceDbSettings:ConnectionString is not configured."); 
    }
    return new MongoClient(settings.ConnectionString); 
});
// Register IMongoSyncRepository as a Scoped service
builder.Services.AddScoped<IMongoSyncRepository, MongoSyncRepository>();

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
// ... (IClientSyncConfigProvider) ...
builder.Services.AddScoped<IClientSyncConfigProvider, EfClientSyncConfigProvider>();

// Register IFhirIngestService 
builder.Services.AddScoped<IFhirIngestService, FhirIngestService>();
//builder.InstallEntityFramework();
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

// 1. Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0); // Default version if not specified
    options.AssumeDefaultVersionWhenUnspecified = true; // Use the default version when no version is specified
    options.ReportApiVersions = true; // Report API versions in the response headers

    // You can choose how to read the API version (e.g., from query string, header, or URL segment)
    // options.ApiVersionReader = new QueryStringApiVersionReader("api-version"); // e.g., ?api-version=1.0
    // options.ApiVersionReader = new HeaderApiVersionReader("X-API-Version"); // e.g., X-API-Version: 1.0
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-API-Version"),
        new UrlSegmentApiVersionReader()); // Enables versioning in URL path (e.g., /v1/resource)
});

builder.Services.AddVersionedApiExplorer(options =>
{
    // Format the version as "'v'major[.minor][-status]"
    options.GroupNameFormat = "'v'VVV";

    // Note: this option is only necessary when versioning by URL segment.
    options.SubstituteApiVersionInUrl = true;
});


builder.InstallSwagger();


builder.InstallApplicationSettings();

builder.InstallDependencyInjectionRegistrations();
builder.Services.AddOpenApi();
builder.InstallCors();

var app = builder.Build();
app.UseSwagger();

// Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
// specifying the Swagger JSON endpoint.
app.UseSwaggerUI(options =>
{
    // Build a swagger endpoint for each discovered API version
    foreach (var description in app.Services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions)
    {
        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", $"FHIR Ingest API {description.GroupName.ToUpperInvariant()}");
    }
    options.RoutePrefix = "swagger"; // Sets the Swagger UI at /swagger
});
//configue kestrel
//builder.Services.Configure<KestrelServerOptions>(builder.Configuration.GetSection("Kestrel"));

//using (var scope = app.Services.CreateScope())
//{
//    var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    EntityFrameworkInstaller.SeedDatabase(appDbContext);
//};


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors(CorsInstaller.DefaultCorsPolicyName);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

//app.UseExceptionHandler();

app.Run();
