# Multi-Client Awareness — Build Plan

Phased plan to move the SeS Transmitter from single-client to client-aware, driven by the
inspection in [`FINDINGS.md`](./FINDINGS.md). Each phase is independently shippable and leaves
the build green.

> **Progress (2026-05-30):** Phase 0 started — test harness live (3 Domain + 10 Infrastructure
> tests passing). First correctness fix (Finding 3.6) done and pinned by tests. Unused `ClientCert`
> config removed from `appsettings.json`. Callback SSRF (5.1) deferred & raised to DevOps
> (`SECURITY-NOTE-callback-ssrf.md`).

## Design contract (non-negotiable)

1. Same client credential across all SHIP target systems (PDS, SCR, …).
2. Credential resolution is **per `clientId`** only — never per target system.
3. `targetSystem` / `ShipService` controls **routing/processing only**.
4. Client-specific secrets must **not** live in instance `appsettings.json`.
5. Client secrets resolve from Vault: `secret/ses/clients/{clientId}/hmac`.
6. Non-secret settings may remain in configuration.

---

## Phase 0 — Test harness & safety net (do first)

Tests must exist before refactoring the auth/processing core. The three test projects are in the
solution but empty.

- [x] `tests/Ship.Ses.Transmitter.Domain.UnitTests` — `FhirSyncRecordTests` (record/collection mapping,
      Pending defaults). *(2026-05-30)*
- [x] `tests/Ship.Ses.Transmitter.Infrastructure.UnitTests` — `FhirRoutingSettingsTests`
      (`ResolveRoute` by service name, by resource, fallback to Default, blank-resource throws) and
      `FhirSyncServiceTests` (success/fail bookkeeping; pins the Finding 3.6 fix). Uses Moq. *(2026-05-30)*
- [x] Flesh out `tests/Ship.Ses.Transmitter.Application.UnitTests` — `ClientCredentialContractTests`
      (per-client credential material, scope excluded, default grant type, value equality) and
      `FhirApiResponseMappingTests` (SHIP-response DTO mapping incl. `FlexibleBundleConverter` tolerance). *(2026-05-31)*
- [x] `FhirApiService` routing + auth-header tests (`FhirApiServiceTests`, `CountingHttpMessageHandler`
      mock): bearer resolved per `record.ClientId`, route-derived scope (+ `AuthSettings` fallback),
      Default vs PDS path shapes, Bundle routing, payload enveloping, HTTP-error → `FhirApiResponse`
      mapping, missing-BaseUrl guard. Token-service caching/single-flight already covered by
      `CachedFhirTokenServiceTests` (the single-identity `TokenService` was deleted in Phase 3). *(2026-05-31)*
- [x] CI `dotnet test` gate (`.github/workflows/tests.yml`) — runs the three test projects directly
      (the .sln references a WebApi project absent from this repo). *(2026-05-31)*
- [ ] Add a shared test project for fakes (`HttpMessageHandler` stub, in-memory stores). *(deferred — fakes
      live in `Infrastructure.UnitTests/Fakes` for now; promote when the Worker integration-test project lands.)*
- [x] Added **characterization tests** that pin today's outbound behaviour (FhirApiService routing/auth +
      response parsing) so the refactor stays observable. *(2026-05-31)*

**Exit:** ✅ `dotnet test` runs with meaningful coverage of routing, token, processing, **outbound HTTP**,
and **response-mapping** paths (3 Domain + 9 Application + 54 Infrastructure green), gated in CI.
**Status:** ✅ done — only the shared-fakes project remains (deferred until the integration-test project).

---

## Phase 1 — Correctness & security fixes (low-risk, high-value)

These are safe to do before the architecture change and de-risk it.

- [x] **Fix success/fail double-write** in `FhirSyncService.ProcessPendingRecordsAsync`
      (Finding 3.6) — a failed record must not also be marked `Synced`. *(2026-05-30, test-pinned)*
- [x] **Increment `RetryCount` + set `LastAttemptAt`** on failure; add max-retry / dead-letter
      (Finding 3.5). *(2026-05-31)* — bounded retry: requeue `Pending` until `MaxSendAttempts` (3), then
      permanent `Failed` (cap-only; no backoff / no separate dead-letter state).
- [x] **Stop logging token prefixes** in `HttpSyncMetricsWriter` (Finding 6.1). *(2026-05-30)*
- [x] **Remove secrets from `appsettings.json`**; load from env/secret store
      (Findings 1.3, 6.2; see `SECRETS-AND-CONFIG.md`). *(2026-05-30)* — ⚠️ **rotation of committed
      secrets still pending (DevOps).**
- [x] **Delete dead `OldProcessPendingRecordsAsync` / `SendAsync1`** (stack-trace persistence, Finding 6.3). *(2026-05-30)*
- [x] Add consistent per-record log scope with `clientId`/`facilityId`/`shipService` (Finding 6.4).
      *(2026-05-30, via `ILogger.BeginScope`)*
- [x] Increment `RetryCount` on failure (Finding 3.5). *(2026-05-30)* — full dead-letter requeue deferred.

**Exit:** no secrets in repo, no token leakage, failure accounting correct, tests cover it.
**Status:** ✅ code-complete. **Outstanding:** rotate the committed secrets (DevOps); add unit tests
for the metrics-writer logging change and the `RetryCount` increment.

---

## Phase 2 — Client credential resolution abstraction

Introduce the seam without changing where credentials come from yet.

- [x] Define `IClientCredentialProvider` + `ClientCredential` in `Application`
      (token endpoint + clientId/secret + grant type; scope excluded — route-derived). *(2026-05-30)*
- [x] Implement `ConfigClientCredentialProvider` (reads `AuthSettings` as the single fallback for
      every clientId) and register it (singleton). *(2026-05-30)*
- [x] Redefine `IFhirTokenService` to take `clientId` + `scope` (Finding 2.4) and implement
      `CachedFhirTokenService` — **per-`(clientId, scope)` cache** with expiry, refresh-ahead (30s)
      and single-flight (`SemaphoreSlim` per key); `TimeProvider`-injectable for deterministic tests.
      Registered as singleton with a `"FhirTokens"` named `HttpClient`. *(2026-05-30)*
- [x] Unit-test cache keying (client/scope), expiry, single-flight concurrency, blank-clientId guard,
      and the credential provider (`CachedFhirTokenServiceTests`, `ConfigClientCredentialProviderTests`). *(2026-05-30)*

**Exit:** ✅ the per-client credential seam + keyed token cache exist, are DI-registered, and are
test-covered (21 Infrastructure tests green). Still single-client-configured.
**Note:** not yet wired into `FhirApiService` — that happens in **Phase 3** (threading `clientId`
through `SendAsync`). `TokenService` remains the active path until then.

---

## Phase 3 — Thread `clientId` through the outbound path

- [x] Add `string clientId` to `IFhirApiService.SendAsync` and all implementations/callers. *(2026-05-30)*
- [x] `FhirSyncService` passes `record.ClientId` (Finding 3.2); `StatusProbeWorker` passes `ev.ClientId`
      (+ `ClientId` added to the probe's missing-identifier guard). *(2026-05-30)*
- [x] `FhirApiService` resolves the token via `IFhirTokenService.GetAccessTokenAsync(clientId, scope)` —
      credential per `clientId`, `route.Scope` for routing; `ResolveRoute` unchanged (Findings 4.1, 4.2).
      `TokenService` (single global identity) **retired/deleted**. *(2026-05-30)*
- [x] Group `ProcessPendingRecordsAsync` by `clientId` (Finding 3.1). *(2026-05-30)* — superseded by
      **round-robin across clients** for fairness (Finding 3.4). *(2026-05-31)*
- [x] Per-client failure isolation: consecutive-failure **circuit breaker** (threshold 3) per client
      group — fast-fails the rest of a failing client's batch without touching other clients (Finding 2.6). *(2026-05-30)*
- [x] mTLS / per-client cert: N/A — dead `ClientCert` config already removed in Phase 1 (Finding 4.3).

**Exit:** ✅ each record is transmitted under its own client's credentials; unit tests prove
two-client isolation and the breaker (`ProcessPendingRecords_OneClientFailing_DoesNotAffectAnotherClient`,
`..._ClientBreaker_OpensAfterConsecutiveFailures_SkipsRest`, `..._SendsUnderEachRecordsOwnClientId`).
**Note:** credentials still resolve from the single `AuthSettings` (Phase 4 swaps in Vault). A dedicated
two-client *integration* test (real host) is deferred to the integration-test project.

---

## Phase 4 — Vault-backed credentials & config cleanup

- [x] Implement `VaultClientCredentialProvider` reading `secret/ses/clients/{clientId}/hmac`
      (Findings 1.2, 2.4): KV v2 over `HttpClient` (`HttpVaultSecretReader`/`IVaultSecretReader`, no new
      NuGet), TTL cache + `Invalidate(clientId)`, merges non-secret `AuthSettings` defaults. *(2026-05-30)*
- [x] Switch DI to the Vault provider, **feature-flagged** via `ClientCredentials:Source`
      (`Config` default keeps the dev fallback). *(2026-05-30)*
- [x] Rename `SeSClient:ClientId` → `SeSClient:TenantId` (legacy `ClientId` still binds as fallback via
      `EffectiveTenantId`); workers + appsettings updated. `AuthSettings:ClientSecret` unused when
      `Source=Vault` (Findings 1.1, 1.5). *(2026-05-30)*
- [x] `appsettings.json` holds only non-secret routing + tenant settings; Vault `Address`/`Token` blank
      in config (supplied via env/secret store) (Finding 9). *(2026-05-30)*

**Exit:** ✅ per-client credentials can be resolved from Vault (feature-flagged), instance config has no
client secrets, tenant vs per-record-client identities are cleanly separated. Tests cover the reader and
provider (35 Infrastructure tests green).
**Note:** 401-triggered eager re-read (call `Invalidate` from the token service on auth failure) and a
live-Vault integration test are sensible follow-ups; rotation is currently picked up within the cache TTL.

---

## Phase 5 — Callback hardening (client-aware)

- [x] Persist `EmrTargetUrl` (= `ClientEMRCallbackUrl`) on the `StatusEvent` at seed time so callback
      resolution doesn't depend on a patient-only lookup (Finding 5.2). *(2026-05-30)*
- [x] **Validate callback URL against an allow-list** before POSTing (Finding 5.1 — SSRF):
      `ICallbackUrlValidator`/`CallbackUrlValidator`, global + per-client hosts, `*.` wildcard, optional
      `RequireHttps`. **Opt-in** via `EmrCallback:Validation:Enabled` (default trust, preserving the
      accepted-risk decision). Disallowed URLs are dead-lettered. *(2026-05-30)*
- [x] Client-aware callback headers: `x-client-id` sent to the EMR (Finding 5.3). Per-client callback
      *auth* (signing/bearer) deferred pending registry support. *(2026-05-30)*
- [x] Dead-letter callbacks after `EmrCallback:MaxAttempts` (`MarkEmrCallbackFailedAsync`,
      `CallbackStatus="Failed"`, excluded from the due-poll) (Finding 5.4). *(2026-05-30)*

**Exit:** ✅ callbacks resolve correctly for non-patient resources (URL persisted on the event), can be
host-validated per client (opt-in), and no longer retry forever. Validator is unit-tested (45 Infra tests).
**Note:** sourcing the allow-list from the client registry (vs config) and per-client callback auth are
future enhancements; enabling enforcement is a DevOps decision (see SECURITY-NOTE).

---

## Phase 6 — Storage abstraction (Postgres-ready)

Decouple persistence from MongoDB so a future Postgres move is an adapter swap.

- [x] Define a storage-neutral interface `IFhirSyncStore` in `Application` using domain types — string
      ids and JSON-string payloads, **no `ObjectId`/`BsonDocument`** (Finding 7.1). `BaseMongoDocument.Id`
      changed to `string` (`[BsonRepresentation(ObjectId)]`, same pattern as `FhirSyncRecord.Id`). Old
      `IMongoSyncRepository` deleted; `Dictionary<ObjectId,(tuple)>` replaced by
      `IReadOnlyDictionary<string, RecordStatusUpdate>`. *(2026-05-31)*
- [x] `MongoSyncRepository` is now the adapter implementing `IFhirSyncStore`; all Mongo specifics
      (ObjectId representation, `BsonDocument` payload parse, collection-per-resource) are confined to it
      (Finding 7.2). *(2026-05-31)*
- [x] "Claim one due item" expressed as `TryClaimEmrCallbackAsync` / `TryClaimStatusProbeAsync`; XML doc
      states the Postgres `SELECT … FOR UPDATE SKIP LOCKED` mapping (Finding 7.4). *(2026-05-31)*
- [x] Interface documents the intended `(clientId, resourceType, status)` + due-poll indexes for any
      backend (Findings 7.3, 7.5). *(2026-05-31)*
- [x] All `IMongoSyncRepository` usages (FhirSyncService, EmrCallbackWorker, StatusProbeWorker,
      FhirIngestService, DI) migrated to `IFhirSyncStore`. *(2026-05-31)*

**Exit:** ✅ workers/services depend on the storage-neutral `IFhirSyncStore`; Mongo is one adapter; a
Postgres adapter is a future drop-in (implement the same interface + a `FOR UPDATE SKIP LOCKED` claim).
Tests pass (3 Domain + 45 Infrastructure). **Note:** the entity types still carry Bson attributes (read
by the Mongo adapter; a Postgres adapter would map them via EF) — full entity/ORM mapping for Postgres is
the remaining work when that migration is actually scheduled.

---

## Test projects — concrete additions

| Project | What to add |
|---|---|
| `Ship.Ses.Transmitter.Domain.UnitTests` | `FhirRoutingSettings.ResolveRoute` matrix; record/status-event invariants. |
| `Ship.Ses.Transmitter.Application.UnitTests` | credential-provider contract, DTO mapping, cache-key builder. |
| `Ship.Ses.Transmitter.Infrastructure.UnitTests` | `TokenService` (handler mock + cache keying), `FhirApiService` (routing + bearer per clientId), `FhirSyncService` (bookkeeping, per-client grouping), Mongo store adapter (Testcontainers/Mongo2Go), callback allowlist validation. |
| *(new)* `Ship.Ses.Transmitter.Worker.IntegrationTests` | two-client end-to-end: each record sent under its own credential; one client's auth failure does not affect the other. |

Shared helpers: `FakeHttpMessageHandler`, in-memory `IClientCredentialProvider`, in-memory stores.

---

## Risk register

| Risk | Mitigation |
|---|---|
| Refactoring auth without tests masks regressions | Phase 0 characterization tests first. |
| Vault outage blocks all clients | Cache credentials; fail per-client, not globally; dev config fallback. |
| Mongo→PG semantics differ (atomic claim) | Encapsulate claim op; design for `SKIP LOCKED`; adapter-level tests. |
| Committed secrets already leaked | Rotate during Phase 1; scrub history if policy requires. |
| Worker TFM mismatch (net8/net9 obj) | Pin Worker TFM explicitly before any csproj change. |
</content>
