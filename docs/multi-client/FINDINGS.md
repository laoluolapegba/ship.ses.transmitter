# SeS Transmitter — Multi-Client Awareness: Code Inspection Findings

> **Scope:** Inspection only. No code was refactored. This report identifies every place
> the transmitter assumes a single client / single auth context / single outbound identity,
> and what must change to support the client-aware design (credentials resolved per
> `clientId`, target system used for routing only, secrets resolved from Vault).
>
> **Branch inspected:** `feature/non-patient-handler`
> **Date:** 2026-05-30

> ### Change log (post-inspection actions)
> - **2026-05-30** — Finding **3.6** (success/fail double-write) **FIXED** + regression tests added
>   (`FhirSyncServiceTests`). See Build Plan Phase 0/1.
> - **2026-05-30** — Findings **1.4 / 4.3**: `ClientCert` blocks removed from `appsettings.json`
>   `FhirRouting` (Default, PDS, SCR) — cert config was unused and would have been client-specific.
> - **2026-05-30** — Finding **5.1** (callback SSRF): **deferred / accepted risk** — we trust the
>   stored URL for now. Raised for DevOps/Security in
>   [`SECURITY-NOTE-callback-ssrf.md`](./SECURITY-NOTE-callback-ssrf.md).
> - **2026-05-30** — Phase 0 test harness started (Domain + Infrastructure unit tests).
> - **2026-05-30 (Phase 1)** — Finding **6.1** token-prefix logging removed (`HttpSyncMetricsWriter`).
>   Finding **6.3** dead methods deleted (`FhirSyncService.OldProcessPendingRecordsAsync` + commented
>   block, `FhirApiService.SendAsync1`). Finding **3.5** `RetryCount` now incremented on failure
>   (dead-letter requeue still deferred). Finding **6.4** per-record log scope
>   (`clientId`/`facilityId`/`shipService`/`resourceId`) added. Findings **1.3 / 6.2** secrets removed
>   from `appsettings.json` → env/secret store; see
>   [`SECRETS-AND-CONFIG.md`](./SECRETS-AND-CONFIG.md) (**committed secrets must be rotated**).
> - **2026-05-30 (Phase 2)** — Client-aware credential seam built: `IClientCredentialProvider` +
>   `ClientCredential` (Application), `ConfigClientCredentialProvider` (single-client fallback), and
>   `CachedFhirTokenService` (per-`(clientId, scope)` cache, expiry/refresh-ahead, single-flight) behind
>   a redefined `IFhirTokenService` (Findings 2.1, 2.2, 2.4). DI-registered + unit-tested.
> - **2026-05-30 (Phase 3)** — `clientId` threaded through `IFhirApiService.SendAsync`; `FhirApiService`
>   now resolves tokens via `IFhirTokenService(clientId, scope)`; `FhirSyncService` passes `record.ClientId`
>   and `StatusProbeWorker` passes `ev.ClientId` (Findings 2.1, 3.2, 4.2). Processing **grouped by clientId**
>   with a per-client **circuit breaker** for isolation (Findings 3.1, 2.6). Single-identity `TokenService`
>   **deleted**. Credentials still come from `AuthSettings` (Vault is Phase 4).
> - **2026-05-30 (Phase 4)** — Per-client credentials from **Vault** (feature-flagged via
>   `ClientCredentials:Source`): `IVaultSecretReader`/`HttpVaultSecretReader` (KV v2 over HttpClient) +
>   `VaultClientCredentialProvider` (TTL cache, merges non-secret `AuthSettings` defaults) reading
>   `secret/ses/clients/{clientId}/hmac` (Findings 1.2, 2.4). `SeSClient:ClientId` → **`TenantId`**
>   (Findings 1.1, 1.5). Config default stays `Config` (single-client) so behaviour is unchanged until flipped.
> - **2026-05-30 (Phase 5)** — Callback hardening: `EmrTargetUrl` persisted on the seeded `StatusEvent`
>   (5.2, fixes non-patient resolution); opt-in `ICallbackUrlValidator` host allow-list (5.1 — default
>   trust, see SECURITY-NOTE); `x-client-id` header (5.3); dead-letter after `EmrCallback:MaxAttempts`
>   via `MarkEmrCallbackFailedAsync` (5.4).
> - **2026-05-31 (Phase 6)** — Storage decoupled from MongoDB: new neutral `IFhirSyncStore` (string ids,
>   JSON payloads) in Application; `MongoSyncRepository` is now the Mongo **adapter**; `IMongoSyncRepository`
>   deleted; `BaseMongoDocument.Id` → `string`; "claim" methods documented for Postgres
>   `FOR UPDATE SKIP LOCKED`. All workers/services migrated (Findings 7.1–7.6).
> - **2026-05-31 (Phase 0 close-out)** — Test coverage completed: `FhirApiServiceTests` (outbound
>   routing/auth-header/scope/enveloping/error-mapping — Findings 2.5, 4.1, 4.2, 4.4), Application
>   `ClientCredentialContractTests` + `FhirApiResponseMappingTests` (DTO mapping), and a CI `dotnet test`
>   gate (`.github/workflows/tests.yml`). Suite: 3 Domain + 9 Application + 54 Infrastructure green.
> - **2026-05-31 (fairness + bounded retry)** — **3.4** processing changed to **round-robin across clients**
>   (one record per active client per round) so a high-volume client can't starve others. **3.5** **bounded
>   retry**: failed sends requeue as `Pending` (incrementing `RetryCount` via `RecordStatusUpdate.IncrementRetry`)
>   until `MaxSendAttempts` (3), then permanent `Failed`; `SyncResultDto.Requeued` added; `TimeSynced` set on
>   success only. Callback SSRF: decision reaffirmed (keep trusting; per-client auth + registry allow-list
>   deferred) — see SECURITY-NOTE. Suite: 3 Domain + 9 Application + 56 Infrastructure green.
> - **2026-06-01 (Vault startup-load + routing/auth cleanup)** — **Outbound auth is no longer
>   shipService-specific.** Per-route `Scope` removed; the outbound scope now lives on `AuthSettings:Scope`
>   (`ship-full-access`) and is used for every SHIP target (Findings 1.4, 2.5, 4.2). `FhirRouting:Default`
>   kept; per-route `ClientCert` and the legacy `FhirApi` cert fields (`ClientCertPath`/`ClientCertPassword`,
>   `FhirClientCertificateSettings`) removed (Findings 1.6, 4.3). **Fail-fast** `ValidateOnStart` for
>   `AuthSettings` (TokenEndpoint/Scope) and each `FhirRouting:Apis` entry (Name/BaseUrl). `ShipAdminAuth`
>   `ClientId` → **`TenantId`** (1.5). **Vault credential model changed to load-at-startup** to match the SeS
>   Ingestor: `VaultClientCredentialProvider` discovers every client by listing the prefix and reads each
>   secret **once** (`InitializeAsync`) into memory — no per-request calls, **no TTL cache**; only active,
>   non-revoked clients are loaded; `FhirSyncService` processes valid clients only; new/rotated clients need a
>   restart (Findings 1.2, 2.4). New `DEPLOYMENT.md` added. Suite: 3 Domain + 9 Application + 63 Infrastructure green.
> - **2026-06-02 (Vault → env-only, Ingestor-style)** — Removed the `ClientCredentials` appsettings section
>   and the `Config` single-client fallback (`ConfigClientCredentialProvider` deleted). Vault is now the
>   **only** outbound-credential source, configured via **OS environment variables** (`VAULT_ADDR`,
>   `VAULT_TOKEN`, `VAULT_MOUNT`, `VAULT_KV_VERSION`, `VAULT_PATH_TEMPLATE`, `VAULT_SECRET_KEY`,
>   `VAULT_REQUEST_TIMEOUT_SECONDS`); `VAULT_ADDR`/`VAULT_TOKEN` are required and the **worker exits at
>   startup** if unset. Dropped the `ClientIdKey` override (folder name is the clientId), the secret-key
>   fallback list, and the **active/revoked/status keys** (a client is loaded if its secret is present;
>   add/remove/rotate ⇒ restart). Startup lists + loads all clients into memory and reports them; only loaded
>   clients are processed (Findings 1.2, 2.4). Docs updated. Suite: 3 Domain + 9 Application + 56 Infrastructure green.
> - **2026-06-02** — New finding **4.5** (non-PDS probe `GET` gap): `StatusProbeWorker` probes via `GET`,
>   but `FhirApiService` only builds a `GET` path for PDS, so non-PDS (e.g. SCR) records with no callback
>   can't be probed and are abandoned — surfaced by an end-to-end ack-half log simulation. Logging tidy:
>   `FhirSyncService` now labels the multi-resource generic pool as `GenericResourceSyncRecord` instead of
>   listing ~140 `[FhirResource]` names.

---

## 0. Executive summary

The transmitter is **structurally single-client today**. A single `SeSClient:ClientId`
("lakeshore") and a single `AuthSettings` credential block are baked into instance
`appsettings.json`, and **every outbound FHIR call uses that one global identity** —
regardless of the `clientId` already stored on each record. The record model is *already*
multi-client aware (`FhirSyncRecord.ClientId`, `StatusEvent.ClientId` are persisted and
carried through processing), but **`clientId` is never used to resolve credentials**.

The good news, matching the design assumptions:

- **`targetSystem` / `shipService` is already routing-only** (`FhirRoutingSettings.ResolveRoute`
  picks endpoint by service/resource, not by credential). Design assumption #3 already holds.
- `clientId`, `facilityId`, `correlationId`, `transactionId`, `shipService` are **preserved**
  end-to-end into `StatusEvent`.

The work, in priority order:

1. **Make outbound auth per-`clientId`** (token service keyed by clientId, resolved from a
   client credential provider → Vault). Highest priority — this is the core of the design.
2. **Remove client secrets from `appsettings.json`** (currently committed in plaintext).
3. **Decouple the data layer from MongoDB** behind an abstraction (for the future Postgres move).
4. **Harden callback handling** (URL is trusted directly from the stored record — SSRF risk).
5. **Per-client fairness/isolation** so one client can neither block nor starve others.

Tenant-level concerns (`ShipAdminApi`, heartbeat, metrics) can stay tenant-scoped per the
design — `SeSClient:ClientId` is effectively a **tenant identity** and should be renamed.

---

## 1. Configuration usage

| # | File / Class | Current behaviour | Single-client assumption | Impact for multi-client | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 1.1 | `appsettings.json` → `SeSClient.TenantId`; `Infrastructure/Settings/SeSClientOptions.cs` | One identity for the whole process; injected into `ResourcesFhirSyncWorker` and `ClientHeartbeatWorker`. | Entire instance == one tenant. | Deployment acts as one tenant for sync-enable/heartbeat/metrics. | ✅ **Done (2026-05-30, Phase 4):** renamed `ClientId` → **`TenantId`** (legacy `ClientId` still binds via `EffectiveTenantId`). This is now explicitly the **tenant** identity; per-record outbound credentials use `record.ClientId` (Phase 3), not this value. | High → resolved |
| 1.2 | `appsettings.json` → `AuthSettings`; `Settings/AuthSettings.cs` | Single outbound FHIR credential block. | One outbound client id + secret for all FHIR calls. | All clients' data transmitted under one SHIP identity. | ✅ **Done (Phases 2–4; finalized 2026-06-02):** per-`clientId` resolution via `IClientCredentialProvider`. `VaultClientCredentialProvider` is now the **only** source — it lists + loads every client (whose secret is present) from `secret/ses/clients/{clientId}` once at startup, configured via **OS env vars** (`VAULT_ADDR`/`VAULT_TOKEN`/`VAULT_*`, Ingestor-style; worker exits if unset). The `Config` fallback and the `ClientCredentials` appsettings section were removed. `AuthSettings` now supplies only non-secret `TokenEndpoint`/`GrantType`/`Scope`; `ClientId`/`ClientSecret` unused. | High → resolved |
| 1.3 | `appsettings.json` — `AuthSettings.ClientSecret`, `ShipAdminAuth.ClientSecret`, DB passwords in `AppSettings.*.ConnectionString` | Real-looking secrets committed in plaintext in the repo. | n/a (security) | Secret leakage; rotating per-client secrets impossible without redeploy. | ✅ **Done (2026-05-30):** blanked in config; supplied via env/secret store ([`SECRETS-AND-CONFIG.md`](./SECRETS-AND-CONFIG.md)). ⚠️ **rotate the committed values — still in git history.** | High (security) → resolved (rotation pending) |
| 1.4 | `Settings/FhirRoutingSettings.cs` + `appsettings.json` → `FhirRouting.Apis[].{Scope, ClientCert, BaseUrl}` | Per-target (`PDS`,`SCR`) routing config including a **single shared** `ClientCert` (`certs/client.pfx`) and per-route `Scope`. | One mutual-TLS cert + scope shared by all clients. | Target endpoints are fine (routing), but the **client certificate is global**, not per-client. | ✅ **Done (2026-05-30; finalized 2026-06-01):** `ClientCert` and per-route `Scope` removed from `FhirRouteSettings` and `appsettings.json` (Default/PDS/SCR). Routing keeps only `BaseUrl`/`TimeoutSeconds`/`CallbackUrlTemplate`/resource lists. **Scope moved to `AuthSettings:Scope`** (auth is not shipService-specific). Any future mTLS/HMAC secret resolves per-client from Vault. | Medium → resolved (config) |
| 1.5 | `Settings/ShipAdminAuthOptions.cs`, `ShipAdminApiOptions.cs` + `appsettings.json` | Admin API auth + heartbeat path use one tenant credential and `{clientId}` path templating. | Tenant-level, single credential. | **Acceptable per design** (admin/heartbeat/metrics are tenant-level). | ✅ **Done (2026-05-30, Phase 4):** kept tenant-scoped; `SeSClient:ClientId` feeding these renamed to `TenantId`. The admin wire contract still sends the value as `clientId`. | Low → resolved |
| 1.6 | `Settings/FhirApiSettings.cs` (legacy `FhirApi` block) + `Program.cs` | Legacy single-endpoint fallback with `ClientCertPath`/`ClientCertPassword`/`CallbackUrlTemplate`. | One base URL + one cert for everything. | Legacy path reinforces single-target/single-cert assumption. | ✅ **Done (2026-06-01):** retained only as a routing fallback (maps `BaseUrl`/`TimeoutSeconds`/`CallbackUrlTemplate` into `Default`); cert fields (`ClientCertPath`/`ClientCertPassword`) and the `FhirClientCertificateSettings` type removed. | Low → resolved |

---

## 2. Authentication / token flow

| # | File / Class | Current behaviour | Single-client assumption | Impact | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 2.1 | ~~`ReadServices/TokenService.cs`~~ (deleted) | Built token request from `IOptions<AuthSettings>`; no `clientId`. Singleton. | One global outbound identity. | Token acquisition ignored `record.ClientId`. | ✅ **FIXED (2026-05-30, Phase 3):** `TokenService` deleted; `FhirApiService` now calls `IFhirTokenService.GetAccessTokenAsync(record.ClientId, scope)`. Credential per client, scope per route. | High → resolved |
| 2.2 | `ReadServices/TokenService.cs` | **No token caching at all** — a fresh token HTTP call on every FHIR send. | n/a | Token endpoint hammered; latency per record; once multi-client, no place to cache per client. | ✅ **Built (2026-05-30):** `CachedFhirTokenService` caches per `(clientId, scope)` with expiry, refresh-ahead (30s) and single-flight. Active once Phase 3 wires it in. | High → built |
| 2.3 | `Security/AdminTokenService.cs` | Caches a **single** token in instance fields (`_cachedToken`, `_expiresAt`) under one lock. | One tenant token. | **OK for tenant-level** admin/metrics/heartbeat. Would be wrong if reused for per-client FHIR. | Leave as tenant token. Do **not** reuse this class for FHIR outbound; build a separate per-client cache. | Low |
| 2.4 | `Security/IFhirTokenService.cs`, `Application/Interfaces/IAccessTokenSource.cs` | Interfaces existed but were **not implemented or wired**; `FhirApiService` depended on concrete `TokenService`. | Abstraction implied a single global token (no clientId param). | The intended seam was unused and single-client shaped. | ✅ **Done (2026-05-30):** `IFhirTokenService` redefined to `GetAccessTokenAsync(clientId, scope, ct)` and implemented by `CachedFhirTokenService`; credential resolution via `IClientCredentialProvider` (Vault impl deferred to Phase 4). Phase 3 injects the interface into `FhirApiService`. (`IAccessTokenSource` still unused — candidate for removal.) | Medium → resolved |
| 2.5 | `ReadServices/FhirApiService.cs` | `scope = route.Scope ?? AuthSettings.Scope`; token from global creds, no clientId. | Token derived from global creds + route scope. | Outbound auth was global. | ✅ **FIXED (2026-05-30, Phase 3; revised 2026-06-01):** `token = _fhirTokenService.GetAccessTokenAsync(clientId, scope)` — credential is client-derived. **Scope is no longer route-derived**: per-route `Scope` was removed and `scope = AuthSettings.Scope` (same authorization for every SHIP target — auth is not shipService-specific). | High → resolved |
| 2.6 | token failure path | Token errors threw; caller caught per-record but identity was global. | One credential → one failure mode for everyone. | A credential outage failed **every** record in the batch. | ✅ **Done (2026-05-30, Phase 3):** per-client tokens isolate failures; `FhirSyncService` adds a per-client **circuit breaker** (3 consecutive failures → skip rest of that client's batch). Other clients unaffected. Test-covered. | High → resolved |

---

## 3. Record processing

| # | File / Class | Current behaviour | Single-client assumption | Impact | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 3.1 | `ReadServices/FhirSyncService.cs` (`ProcessPendingRecordsAsync`) | `GetByStatusAsync<T>("Pending")` loads pending records across all clients; processed in one loop. | Processing was client-agnostic. | No per-client grouping/isolation. | ✅ **Done (2026-05-30, Phase 3):** records now `GroupBy(ClientId)`; each group processed with its own circuit breaker. (Per-client fair *batching/quotas* still a future enhancement — see 3.4.) | High → resolved |
| 3.2 | `FhirSyncService.cs` | `SendAsync(...)` was called without `record.ClientId`. | clientId dropped before the HTTP/auth layer. | The single most important gap. | ✅ **FIXED (2026-05-30, Phase 3):** `clientId` added to `IFhirApiService.SendAsync`; `FhirSyncService` passes `record.ClientId` (and `StatusProbeWorker` passes `ev.ClientId`). | High → resolved |
| 3.3 | `FhirSyncService.cs` (StatusEvent seeding) | `clientId`, `facilityId`, `correlationId`, `shipService` **are** copied into `StatusEvent`. | n/a — this is correct. | Tracing fields are preserved (good). | Keep; ensure `transactionId` always set. No change. | — (good) |
| 3.4 | `Persistance/.../MongoSyncRepository.cs:63` `GetByStatusAsync` (default `take=100`) | `ProcessPendingRecordsAsync` calls it with **no paging args** → fixed 100, no ordering, no per-client cap. | One client → fairness irrelevant. | A high-volume client can monopolize each batch and **starve** other clients. | ✅ **Done (2026-05-31):** processing is now **round-robin across clients** — one record per active client per round (was drain-one-client-then-next), so a high-volume client can no longer monopolize a batch. Per-client circuit breaker preserved. (Per-client *quotas*/global cap still a future tuning lever.) | Medium → resolved |
| 3.5 | `FhirSyncRecord.RetryCount` (Domain) + `MongoSyncRepository.BulkUpdateStatusAsync` | `RetryCount` was set to `0` on ingest and **never incremented**. `BulkUpdateStatusAsync` set status/error/txn/timeSynced/lastAttemptAt but not retryCount. | n/a (latent bug) | Retries not tracked; attempts not observable/boundable. | ✅ **Done (2026-05-31):** **bounded retry** — a failed send is now requeued (left `Pending`) and `RetryCount` incremented (via `RecordStatusUpdate.IncrementRetry`); on the **3rd** attempt (`MaxSendAttempts`) it becomes permanently `Failed`. No backoff / no separate dead-letter state (per "cap attempts only"). `TimeSynced` now set on success only. `SyncResultDto` gained `Requeued`. | Medium → resolved |
| 3.6 | `FhirSyncService.cs` (`ProcessPendingRecordsAsync`) | Duplicate write: success path added the same `ObjectId` to `successUpdates` **twice**, and even failed records got a later unconditional "Synced" success write + duplicate staging mark. | n/a (correctness bug) | Failed records could be overwritten as "Synced"; masked failures and corrupted per-client metrics. | ✅ **FIXED (2026-05-30):** removed the unconditional "mark Synced" block; success/fail are now mutually exclusive. Pinned by `FhirSyncServiceTests` (rejected ⇒ `Synced=0, Failed=1`). | High (correctness) → resolved |

---

## 4. Outbound FHIR / API routing

| # | File / Class | Current behaviour | Single-client assumption | Impact | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 4.1 | `FhirRoutingSettings.ResolveRoute` | Endpoint chosen by `shipService` name, else by resource type, else `Default`. | None — routing only. | **Correct per design #3** (targetSystem = routing only, not credentials). | Keep. This is the model to preserve. | — (good) |
| 4.2 | `FhirApiService.SendAsync` | Token + scope were global; same bearer reused for every client/target. | One token for all clients/targets. | All clients shared one outbound identity. | ✅ **FIXED (2026-05-30, Phase 3; revised 2026-06-01):** token resolved by `clientId` via `IFhirTokenService` (cached per `(clientId, scope)`); same client credential **and same scope** (`AuthSettings.Scope`) across PDS/SCR per design #1–#3 — authorization is not shipService-specific. | High → resolved |
| 4.3 | `Installers/WorkerServiceExtensions.cs` (`AddFhirApiClient`) | One named `HttpClient "FhirApi"` with `Default.BaseUrl` + single timeout; **client certificate from config was never applied** to the handler. | One HTTP client / one cert for all targets+clients. | mTLS cert config was dead (not wired); per-client/per-target cert impossible. | ✅ **Done (2026-05-30; finalized 2026-06-01):** dead `ClientCert` removed from `appsettings.json` **and** the `ClientCert`/`FhirClientCertificateSettings` types deleted (no longer needed by the legacy binder). If mTLS is later required, wire a handler that selects the **per-client** cert from Vault. | Medium → resolved (config) |
| 4.4 | `FhirApiService.SendAsync` PDS branch (`:74`) | Hardcoded `"PDS"` string comparison and hardcoded `/api/v1/...` path shapes. | Target-specific logic embedded in code. | Adding/altering targets needs code changes; brittle. | Drive path templates from routing config; keep credential resolution separate (per client). | Low |
| 4.5 | `FhirApiService.SendAsync` (non-PDS branch) + `StatusProbeWorker.ProbeOneAsync` | For non-PDS services the endpoint switch supports **POST only** (`_ => throw NotSupportedException`); the only `GET` path shape is the PDS one (`/api/v1/{type}/{id}`). But `StatusProbeWorker` resolves status by issuing a **`GET`** (`_fhir.SendAsync(FhirOperation.Get, …, shipService: ev.ShipService)`). | `GET` path shapes defined for PDS only. | A non-PDS (e.g. **SCR**) record that receives **no callback** can't be probed: the `GET` throws `NotSupportedException`, `ProbeOneAsync` retries then **abandons**, and its `StatusEvent` stays `PENDING`/`Abandoned` (never `SUCCESS`). Status resolution for non-PDS therefore relies **entirely on the async callback**; a missed callback is never recovered by the probe. Found via the ack-half simulation (2026-06-02). | Define a `GET` path shape for non-PDS routes (or make probe path/method routing config-driven), or short-circuit probing for services with no `GET` contract (mark such events so they aren't repeatedly attempted/abandoned). | Medium |

---

## 5. Callback handling

| # | File / Class | Current behaviour | Single-client assumption | Impact | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 5.1 | `Worker/EmrCallbackWorker.cs` (`ResolveTargetUrlAsync`) | Callback URL taken directly from the stored event/record and POSTed with no validation. | Trusts stored URL implicitly. | **SSRF / open-redirect risk**. | 🟡 **Mechanism shipped (2026-05-30, Phase 5), enforcement opt-in.** `ICallbackUrlValidator` always rejects non-absolute/non-http(s) URLs; host allow-list (global + per-client, `*.` wildcard, optional https) gated by `EmrCallback:Validation:Enabled` (**default trust** — preserves the accepted-risk decision). Disallowed → dead-letter. DevOps decides when to enforce; see [`SECURITY-NOTE-callback-ssrf.md`](./SECURITY-NOTE-callback-ssrf.md). | High (security) — guard in place, enforce = DevOps |
| 5.2 | `EmrCallbackWorker.ResolveTargetUrlAsync` | Fallback URL lookup used `GetPatientByTransactionIdAsync` (patient collection only). | Assumed every event originates from a patient record. | For non-patient resources the lookup missed → endless "Missing EMR callback URL". | ✅ **Done (2026-05-30, Phase 5):** `EmrTargetUrl` is persisted on the `StatusEvent` when the PENDING event is seeded (`FhirSyncService.TrySeedPendingAsync`), so resolution no longer depends on the patient-only lookup (which remains a last-resort fallback). | Medium → resolved |
| 5.3 | `EmrCallbackWorker.SendToEmrAsync` | Single `"EmrCallback"` `HttpClient`; correlation headers only. | One callback transport/identity. | If EMR endpoints need per-client auth, not supported. | 🟡 **Partial (2026-05-30, Phase 5):** `x-client-id` header now sent for traceability. Per-client callback **auth** (signing/bearer) deferred pending client-registry support. | Medium |
| 5.4 | `EmrCallbackWorker` retry/backoff | Per-event exponential backoff + in-flight claim, but **no cap** → poison events retried forever. | None (isolation was fine). | Poison/disallowed callbacks retried indefinitely. | ✅ **Done (2026-05-30, Phase 5):** dead-letter after `EmrCallback:MaxAttempts` (`MarkEmrCallbackFailedAsync` → `CallbackStatus="Failed"`, excluded from the due-poll). | Low → resolved |

---

## 6. Logging / audit

| # | File / Class | Current behaviour | Issue | Impact | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 6.1 | `AdminApi/HttpSyncMetricsWriter.cs` | Logged `access token: {first 10 chars}...` on every metrics/status write. | Partial token written to logs. | Token-prefix leakage to log sinks (Console/File/Elastic). | ✅ **FIXED (2026-05-30):** removed; now only a warning when **no** token is acquired (no material logged). | High (security) → resolved |
| 6.2 | `appsettings.json` | Secrets in plaintext (see 1.3). | Secrets at rest in repo + likely in container images. | Leakage. | ✅ **Done (2026-05-30):** blanked in config, supplied via env/secret store ([`SECRETS-AND-CONFIG.md`](./SECRETS-AND-CONFIG.md)). ⚠️ **committed values still in git history — rotate.** | High (security) → resolved (rotation pending) |
| 6.3 | `FhirSyncService.OldProcessPendingRecordsAsync` | On exception, persisted `ex.StackTrace` into `apiResponsePayload` (stored in Mongo). | Sensitive internals persisted to DB. | Stack traces stored and later possibly echoed to EMR. | ✅ **FIXED (2026-05-30):** dead method deleted (also `FhirApiService.SendAsync1` + trailing commented block). The active path stores `ex.Message` only — keep stack traces to logs. | Medium → resolved |
| 6.4 | `FhirApiService` / `FhirSyncService` send logs | Logged `shipService`, `resourceType`, `resourceId`, `transactionId`, `correlationId` in most places — but **not consistently `clientId`/`facilityId`**. | Inconsistent audit dimensions. | Hard to trace a record to its client in logs once multi-client. | ✅ **Done (2026-05-30):** `FhirSyncService` wraps each record in `ILogger.BeginScope` with `ClientId`/`FacilityId`/`ShipService`/`ResourceId`, so all per-record logs + seeded events carry them. | Medium → resolved |
| 6.5 | `TokenService` logging | Masks `clientId`, masks/omits `access_token` in success bodies. | n/a — correct. | Good. | Keep; apply the same masking helper everywhere tokens appear. | — (good) |

---

## 7. MongoDB repository assumptions (Postgres-migration impact)

| # | File / Class | Current behaviour | Mongo coupling | Impact for Postgres | Recommended change | Priority |
|---|---|---|---|---|---|---|
| 7.1 | `IMongoSyncRepository` + `MongoSyncRepository` | Entire persistence API was Mongo-typed (`ObjectId`, `BsonDocument`, …). | Hard, throughout. | A Postgres move required rewriting every call site. | ✅ **Done (2026-05-31):** storage-neutral **`IFhirSyncStore`** (string ids, JSON payloads, `RecordStatusUpdate`) introduced in Application; `IMongoSyncRepository` deleted; `BaseMongoDocument.Id` → `string`. No Mongo types cross the boundary. | High → resolved |
| 7.2 | `MongoSyncRepository` — collection-per-resource | Each resource type → its own Mongo collection. | Schema-per-collection model. | Postgres would use tables + `resource_type`/`client_id` columns. | ✅ **Done (2026-05-31):** `MongoSyncRepository` is now the **adapter**; collection-per-resource is confined to it. A Postgres adapter implements `IFhirSyncStore` with tables instead. | High → resolved (adapter) |
| 7.3 | Status polling — `FetchDueStatusProbesAsync`, `FetchDueEmrCallbacksAsync` | Mongo `Filter`/`Sort`/`Limit` queries. | Mongo query builders. | Re-express as SQL with indexes. | ✅ **Abstracted (2026-05-31):** these are now `IFhirSyncStore` methods; the interface documents the `(clientId, resourceType, status)` + due-poll index intent. SQL impl is the Postgres adapter's job. | Medium → resolved (abstracted) |
| 7.4 | Atomic claim — `TryClaim…` (was `TryMark…InFlight`) | `UpdateOneAsync` conditional filter; `ModifiedCount == 1`. | Mongo conditional update. | Postgres needs `FOR UPDATE SKIP LOCKED`. | ✅ **Done (2026-05-31):** renamed `TryClaimEmrCallbackAsync`/`TryClaimStatusProbeAsync` on the interface, XML-documented to map to `SELECT … FOR UPDATE SKIP LOCKED` on Postgres. | Medium → resolved |
| 7.5 | Field updates — `BulkUpdateStatusAsync` | Mongo `Update.Set` bulk write; `retryCount` not updated. | Mongo bulk update. | Map to SQL `UPDATE`; add `retry_count`. | ✅ **Done (2026-05-31):** typed `RecordStatusUpdate` DTO on the interface (no tuple); `RetryCount` increment added in Phase 1. Both adapters honor the DTO. | Medium → resolved |
| 7.6 | `GetPatientByTransactionIdAsync` hardcodes `"transformed_pool_patients"` | Literal collection + patient-only assumption (see 5.2). | Carries the patient-only bug into any backend. | 🟡 **Mitigated (2026-05-31):** `EmrTargetUrl` now persisted on the event (Phase 5.2) so the patient lookup is only a last-resort fallback. The literal remains inside the Mongo adapter (no longer leaks); generic resolution is a future cleanup. | Medium |

---

## 8. Direct answers to the "pay special attention" questions

| Question | Finding |
|---|---|
| **Must `AuthSettings` become dynamically resolved per `clientId`?** | **Yes.** Today `AuthSettings` is a single `IOptions`-bound block used by the singleton `TokenService` for all FHIR calls. It must become a per-client credential resolution (Vault `secret/ses/clients/{clientId}/hmac`), with `AuthSettings` reduced to non-secret defaults/fallback. |
| **Must token caching become keyed by `clientId`?** | **Yes.** `TokenService` currently does **no** caching; `AdminTokenService` caches one global tenant token. Outbound FHIR needs a cache keyed by `(clientId, scope)` with expiry. |
| **Does outbound auth assume one global SeS identity?** | **Yes.** `FhirApiService` → `TokenService` authenticates as the single `AuthSettings.ClientId` for every record/client/target. `record.ClientId` is never used for auth. |
| **Is `targetSystem` incorrectly coupled to credentials?** | **No.** `FhirRoutingSettings.ResolveRoute` uses `shipService`/resource for **routing only** (endpoint, scope, timeout). Credentials are global *not because of target coupling* but because there is no per-client resolution at all. Preserve this separation; only the credential lookup needs to change (per `clientId`, shared across targets per design #1–#2). |

---

## 9. Future target direction (confirmed feasible)

- **Transmitter resolves outbound auth dynamically using stored `clientId`** → add `clientId` to
  `IFhirApiService.SendAsync` and to the token service; pass `record.ClientId`. (Findings 2.1, 3.2, 4.2)
- **Client-specific secret/config from Vault/registry** → new `IClientCredentialProvider` with
  Vault adapter (`secret/ses/clients/{clientId}/hmac`), cached per client. (Findings 1.2, 2.4)
- **`targetSystem`/`ShipService` controls routing only** → already true; keep `FhirRoutingSettings`. (Finding 4.1)
- **SeS instance config no longer contains client-specific secrets** → strip secrets from
  `appsettings.json`; keep only non-secret routing + tenant settings. (Findings 1.2, 1.3, 6.2)
</content>
