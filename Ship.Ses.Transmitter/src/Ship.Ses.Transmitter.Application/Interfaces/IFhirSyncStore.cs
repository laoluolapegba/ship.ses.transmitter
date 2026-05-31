using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ship.Ses.Transmitter.Domain.Patients;
using Ship.Ses.Transmitter.Domain.Sync;

namespace Ship.Ses.Transmitter.Application.Interfaces
{
    /// <summary>
    /// Storage-neutral persistence for FHIR sync records and status events.
    /// <para>
    /// Identifiers are opaque <see cref="string"/>s and payloads are JSON strings — no MongoDB types
    /// (<c>ObjectId</c>/<c>BsonDocument</c>) cross this boundary — so a PostgreSQL adapter can implement
    /// the same contract as a drop-in replacement for the Mongo adapter.
    /// </para>
    /// <para>
    /// The <c>TryClaim…</c> methods are single-item atomic claims used by the workers to avoid double
    /// processing. Mongo implements them as a conditional update; a Postgres adapter should implement
    /// them with <c>SELECT … FOR UPDATE SKIP LOCKED</c>. Queries are intended to be served by indexes on
    /// <c>(clientId, resourceType, status)</c> and on the due-poll fields.
    /// </para>
    /// </summary>
    public interface IFhirSyncStore
    {
        Task<IEnumerable<T>> GetPendingRecordsAsync<T>() where T : FhirSyncRecord, new();
        Task AddRecordAsync<T>(T record) where T : FhirSyncRecord;
        Task UpdateRecordAsync<T>(T record) where T : FhirSyncRecord;
        Task<IEnumerable<T>> GetByStatusAsync<T>(string status, int skip = 0, int take = 100) where T : FhirSyncRecord, new();

        /// <summary>Bulk status update keyed by record id (string).</summary>
        Task BulkUpdateStatusAsync<T>(IReadOnlyDictionary<string, RecordStatusUpdate> updates) where T : FhirSyncRecord, new();

        Task<PatientSyncRecord?> GetPatientByTransactionIdAsync(string transactionId, CancellationToken ct = default);
        Task InsertStatusEventAsync(StatusEvent ev, CancellationToken ct = default);

        // ── EMR callback delivery ──
        Task<List<StatusEvent>> FetchDueEmrCallbacksAsync(int batchSize, CancellationToken ct = default);
        /// <summary>Atomically claim a due callback (Postgres: FOR UPDATE SKIP LOCKED).</summary>
        Task<bool> TryClaimEmrCallbackAsync(string id, CancellationToken ct = default);
        Task MarkEmrCallbackSucceededAsync(string id, int statusCode, string? body, string? targetUrl, CancellationToken ct = default);
        Task MarkEmrCallbackRetryAsync(string id, string? error, TimeSpan delay, string? targetUrl, CancellationToken ct = default);
        Task MarkEmrCallbackFailedAsync(string id, string? error, string? targetUrl, CancellationToken ct = default);

        // ── Status probing ──
        Task<List<StatusEvent>> FetchDueStatusProbesAsync(TimeSpan age, int batchSize, CancellationToken ct = default);
        /// <summary>Atomically claim a due probe (Postgres: FOR UPDATE SKIP LOCKED).</summary>
        Task<bool> TryClaimStatusProbeAsync(string id, CancellationToken ct = default);
        Task MarkProbeSucceededAsync(string id, CancellationToken ct = default);
        Task MarkProbeRetryAsync(string id, string? error, TimeSpan delay, bool abandon, CancellationToken ct = default);
        /// <summary>Promote a PENDING event to SUCCESS and attach the probe payload (JSON).</summary>
        Task MarkProbeSuccessAndAttachPayloadAsync(string id, string message, string? payloadJson, CancellationToken ct = default);
    }

    /// <summary>A status mutation to apply to a sync record (storage-neutral; replaces a MongoDB tuple).</summary>
    public sealed record RecordStatusUpdate(string Status, string Message, string TransactionId, string RawResponse);
}
