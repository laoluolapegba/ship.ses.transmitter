using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Extensions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Org.BouncyCastle.Utilities;
using Ship.Ses.Transmitter.Application.Interfaces;
using Ship.Ses.Transmitter.Application.Sync;
using Ship.Ses.Transmitter.Domain.Enums;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;
using Ship.Ses.Transmitter.Infrastructure.Services;
using Ship.Ses.Transmitter.Infrastructure.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Infrastructure.ReadServices
{
    public class FhirSyncService : IFhirSyncService
    {
        private readonly IFhirSyncRepository _repository;
        private readonly ILogger<FhirSyncService> _logger;
        private readonly IFhirApiService _fhirApiService;

        public FhirSyncService(IFhirSyncRepository repository, ILogger<FhirSyncService> logger, IFhirApiService fhirApiService)
        {
            _repository = repository;
            _logger = logger;
            _fhirApiService = fhirApiService;
        }

        public async Task<SyncResultDto> ProcessPendingRecordsAsync<T>(CancellationToken token) where T : FhirSyncRecord, new()
        {
            //var records = await _repository.GetPendingRecordsAsync<T>();

            var result = new SyncResultDto();

            var records = await _repository.GetByStatusAsync<T>("Pending");
            result.Total = records.Count();

            var successIds = new List<string>();
            var failedIds = new List<string>();


            foreach (var record in records)
            {
                try
                {
                    _logger.LogInformation("📤 Syncing {Type} with ID {Id}", typeof(T).Name, record.ResourceId);

                    //var result = await _fhirApiService.SendAsync(
                    //    FhirOperation.Post,
                    //    record.ResourceType,
                    //    record.ResourceId,
                    //    record.FhirJson.ToCleanJson(),
                    //    token);

                    var response = await _fhirApiService.SendAsync(
                        FhirOperation.Post,
                        record.ResourceType,
                        record.ResourceId,
                        record.FhirJson.ToCleanJson(),
                        token);
                    record.Status = "Synced";
                    record.TimeSynced = DateTime.UtcNow;
                    record.ErrorMessage = null;

                    successIds.Add(record.Id);
                    _logger.LogInformation("✅ Sync success for {Id}", record.ResourceId);
                }
                catch (Exception ex)
                {
                    record.ErrorMessage = ex.Message;
                    failedIds.Add(record.Id);
                    _logger.LogError(ex, "❌ Sync failed for {Id}: {Message}", record.ResourceId, ex.Message);
                }
            }
            if (successIds.Any())
            {
                var objectIds = successIds.Select(id => ObjectId.Parse(id)).ToList();
                await _repository.BulkUpdateStatusAsync<T>(objectIds, "Failed");
            }
                
            //await _repository.BulkUpdateStatusAsync<T>(successIds, "Synced");

            if (failedIds.Any())
            {
                var objectIds = failedIds.Select(id => ObjectId.Parse(id)).ToList();
                await _repository.BulkUpdateStatusAsync<T>(objectIds, "Failed");
            }
                //await _repository.BulkUpdateStatusAsync<T>(failedIds, "Failed", "See logs");

            result.Synced = successIds.Count;
            result.Failed = failedIds.Count;
            //result.FailedIds = failedIds.Select(x => x.ToString()).ToList();
            result.FailedIds = failedIds.ToList();
            return result;
        }
        //public async Task ProcessPendingRecordsAsync<T>(CancellationToken cancellationToken) where T : FhirSyncRecord
        //{
        //    _logger.LogInformation($"Getting pending records ...");
        //    var records = await _repository.GetPendingRecordsAsync<T>();


        //    _logger.LogInformation($"Got {records.Count()} pending records from {resourceType.ToString()}...");
        //    foreach (var record in records) 
        //    {
        //        if (cancellationToken.IsCancellationRequested)
        //            break;

        //        try
        //        {
        //            _logger.LogInformation($"Processing {record.ResourceId}... from {resourceType.ToString()}");
        //            // Simulate FHIR API Call
        //            //string jsonPayload = record.FhirJson.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson });
        //            string jsonPayload = record.FhirJson.ToCleanJson();
        //            await _fhirApiService.SendAsync(
        //                FhirOperation.Post,
        //                resourceType: "Patient",
        //                resourceId: record.ResourceId,
        //                jsonPayload: jsonPayload,
        //                cancellationToken: cancellationToken
        //            );
        //            await Task.Delay(1000, cancellationToken);

        //            record.Status = "Synced";
        //            record.TimeSynced = DateTime.UtcNow;
        //            await _repository.UpdateRecordAsync(record);
        //            _logger.LogInformation($"Updated resource {record.ResourceId} ..."); 

        //        }
        //        catch (Exception ex)
        //        {
        //            record.RetryCount++;
        //            record.ErrorMessage = ex.Message;
        //            await _repository.UpdateRecordAsync(record);
        //        }
        //    }
        //}
    }
}
