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

        /// <summary>
        /// Ensures the status-event store's schema/indexes exist — notably a <b>unique constraint on
        /// transactionId</b> (Mongo: a partial unique index over non-empty transactionIds; Postgres: a partial
        /// unique index) so the transmitter probe flow and the real SHIP callback converge on a single document
        /// per transaction instead of racing to create duplicates. Idempotent; safe to call once at startup.
        /// </summary>
        Task EnsureStatusEventSchemaAsync(CancellationToken ct = default);

        /// <summary>
        /// Inserts the initial PENDING status event for a send, keyed by transactionId, <b>only if one does
        /// not already exist</b>. If an event for the same transactionId is already present (a re-seed, or the
        /// real SHIP callback landed first), this is a no-op — the flows converge on that one document rather
        /// than creating a duplicate that would trigger a second EMR callback.
        /// </summary>
        Task SeedPendingStatusEventAsync(StatusEvent ev, CancellationToken ct = default);

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
        /// <summary>
        /// Promote a <b>still-PENDING</b> event to SUCCESS and attach the probe payload (JSON). The update is
        /// guarded by the current status: if the real SHIP callback already resolved the event to a terminal
        /// status, nothing is changed and this returns <c>false</c> (the probe stands down rather than
        /// overwriting authoritative callback data or re-triggering delivery). Returns <c>true</c> when the
        /// probe result was the one that promoted the event.
        /// <para>
        /// <paramref name="shipId"/> is the SHIP identifier extracted from the probe response; when present it
        /// is persisted onto the event so the EMR callback carries the same <c>shipId</c> a real SHIP callback
        /// would. When null/blank the existing value is left untouched.
        /// </para>
        /// </summary>
        Task<bool> MarkProbeSuccessAndAttachPayloadAsync(string id, string message, string? payloadJson, string? shipId, CancellationToken ct = default);
    }

    /// <summary>A status mutation to apply to a sync record (storage-neutral; replaces a MongoDB tuple).</summary>
    /// <param name="IncrementRetry">
    /// When true the adapter increments the record's attempt counter (RetryCount). Set for every failed
    /// send attempt — whether the record is requeued (<c>Pending</c>) or permanently failed (<c>Failed</c>) —
    /// so bounded retry can cap attempts. Left false for successful syncs.
    /// </param>
    public sealed record RecordStatusUpdate(
        string Status, string Message, string TransactionId, string RawResponse, bool IncrementRetry = false);
}
