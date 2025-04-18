using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ship.Ses.Transmitter.Domain;
using Ship.Ses.Transmitter.Domain.Sync;
using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Context;
using Ship.Ses.Transmitter.Infrastructure.Settings;
using Ship.Ses.Transmitter.Application.Sync;

namespace Ship.Ses.Transmitter.Worker
{
    public class MetricsSyncReporterWorker : BackgroundService
    {
        private readonly ILogger<MetricsSyncReporterWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        //private readonly ISyncMetricsCollector _collector;
        //private readonly ISyncMetricsWriter _writer;
        private readonly string _clientId;

        public MetricsSyncReporterWorker(
            ILogger<MetricsSyncReporterWorker> logger,
            //ISyncMetricsCollector collector,
            //ISyncMetricsWriter writer,
             IServiceProvider serviceProvider,
            IOptions<SeSClientOptions> options)
        {
            _logger = logger;
            //_collector = collector;
            //_writer = writer;
            _serviceProvider = serviceProvider;
            _clientId = options.Value.ClientId;

            var correlationId = Guid.NewGuid().ToString(); // Or from a header if available

            using (LogContext.PushProperty("correlationId", correlationId)) ;
            using (LogContext.PushProperty("clientId", _clientId)) ;
            using (LogContext.PushProperty("component", "MetricsSyncReporterWorker")) ;
            //using (LogContext.PushProperty("resource", "Patient")) ;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("📡 SyncMetricsReporter started for client: {ClientId}", _clientId);

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var collector = scope.ServiceProvider.GetRequiredService<ISyncMetricsCollector>();
                    var writer = scope.ServiceProvider.GetRequiredService<ISyncMetricsWriter>();

                    try
                    {
                        var status = await collector.CollectStatusAsync(_clientId);
                        var metrics = await collector.CollectMetricsAsync(_clientId);
                        //var statts = new SyncClientStatus { ClientId = _clientId };
                        _logger.LogInformation("Writing current sync status: {status}", JsonSerializer.Serialize(status));
                        await writer.WriteStatusAsync(status);
                        await writer.WriteMetricsAsync(metrics); // ✅ bulk write
                        //foreach (var metric in metrics)
                        //{
                        //    await writer.WriteMetricAsync(metric);
                        //}

                        _logger.LogInformation("✅ Sync metrics written for {ClientId}", _clientId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to write sync metrics");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

                }


                
            }
        }
    }
}