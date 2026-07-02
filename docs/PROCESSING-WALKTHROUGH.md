# SeS Transmitter — End-to-End Processing Walkthrough

How a single transformed FHIR record travels through the Transmitter: from being picked up in the
staging store, authenticated and sent to SHIP, through to the acknowledgement being delivered back to
the client's EMR. This is the outbound half of the SHIP SeS integration.

> **Key idea up front — the Transmitter never blocks waiting for SHIP's final result.**
> SHIP answers the outbound `POST` synchronously with **`202 Accepted` + a `transactionId`** — that's
> the "I've queued it" acknowledgement, **not** the final clinical outcome. The real result arrives
> later, asynchronously: either SHIP calls back (received by the companion **Ingestor**, which writes a
> terminal status event — `SUCCESS`, `ERROR`, `REJECTED`, `CONFLICT` or `DUPLICATE`) or the Transmitter's
> **probe** worker pulls it. So the flow has two halves:
>
> - **Send half** — `ResourcesFhirSyncWorker → FhirSyncService → FhirApiService` (Stages 1–9).
> - **Acknowledgement half** — the `StatusEvent` state machine, `StatusProbeWorker`, `EmrCallbackWorker`
>   (Stages 10–11).

---

## Contents

- [Components](#components)
- [Stage 0 — Startup: load the client credentials from config](#stage-0--startup-load-the-client-credentials-from-config)
- [The send half (Stages 1–9)](#the-send-half)
- [The acknowledgement half (Stages 10–11)](#the-acknowledgement-half)
- [State-machine summary](#state-machine-summary)
- [Known gaps](#known-gaps)

---

## Components

| Type | Role |
|---|---|
| `Worker/ResourcesFhirSyncWorker` | Background loop: pulls `Pending` records and drives sending. |
| `Infrastructure/ReadServices/FhirSyncService` | Orchestrates a batch: fairness, retry, status seeding. |
| `Infrastructure/Services/FhirApiService` | Builds and sends the outbound FHIR HTTP call. |
| `Infrastructure/Security/CachedFhirTokenService` | Acquires + caches outbound bearer tokens per `(clientId, scope)`. |
| `Infrastructure/Security/ConfigClientCredentialProvider` | Loads per-client secrets from config (`AppSettings:Clients`, ISW-injected) at startup; resolves them from memory. |
| `Application/Interfaces/IFhirSyncStore` (Mongo adapter `MongoSyncRepository`) | Storage-neutral persistence for records + status events. |
| `Worker/StatusProbeWorker` | Probes SHIP for records that got no callback within a timeout. |
| `Worker/EmrCallbackWorker` | Delivers **terminal** results (`SUCCESS`/`ERROR`/`REJECTED`/`CONFLICT`/`DUPLICATE`) back to the client's EMR callback URL. |

Two record pools live in MongoDB: `PatientSyncRecord` (`transformed_pool_patients`) and
`GenericResourceSyncRecord` (`transformed_pool_resources`, all non-patient resource types). Status
events live in `fhirstatusevents`.

---

## Stage 0 — Startup: load the client credentials from config

Before any worker processes a record, `Program.cs` calls `IClientCredentialProvider.InitializeAsync()`
once. Outbound credentials come from **configuration** — the `AppSettings:Clients` list. The application
makes **no Vault API calls**: it knows no Vault address/token and handles no `X-Vault-Token`. The secret
**values** are injected into the process environment before startup by the org's **ISW secret-injection
mechanism** (a HashiCorp Vault agent/sidecar) and bound over the committed placeholders by .NET's
environment-variable configuration provider. The startup sequence:

1. **Read** `AppSettings:Clients` from configuration (placeholders now overridden by the ISW-injected env
   vars `AppSettings__Clients__{n}__ClientSecret` / `__HmacSecret`).
2. **Keep** only entries with `Status = ACTIVE` and a non-blank `ClientSecret`; the entry's `ClientId`
   **is** the outbound `client_id`.
3. **Load into memory** as `ClientCredential(TokenEndpoint, clientId, clientSecret, GrantType, hmacSecret)`
   — `clientId`/`clientSecret`/`hmacSecret` come from the client entry; `TokenEndpoint`/`GrantType` come
   from `AuthSettings`.
4. **Report** the result in the log:
   `🔐 Client credential load complete: 3 client(s) loaded, 0 skipped. Loaded: …`.

There are **no per-request lookups and no TTL** — every credential is resolved from this in-memory set
thereafter. **Only loaded clients are processed** (Stage 2). Adding, removing or rotating a client requires
a **restart**.

### Configuration shape

`appsettings.json` carries the non-secret structure and placeholders; the ISW injector supplies the real
secret values as environment variables at runtime.

```jsonc
"AppSettings": {
  "Hmac": { "Enabled": true, "SignatureHeader": "X-SHIP-Signature", "HmacAlgo": "HMACSHA256", … },
  "Clients": [
    { "ClientId": "ses-client-a", "ClientSecret": "<placeholder>", "HmacSecret": "<placeholder>", "Status": "ACTIVE" }
    // ses-client-b, …
  ]
}
```

| Config key | Secret? | Source | Meaning |
|---|---|---|---|
| `AppSettings:Clients:{n}:ClientId` | No | `appsettings.json` | Client identity; the outbound `client_id` and the value carried on each record. |
| `AppSettings:Clients:{n}:ClientSecret` | **Yes** | ISW env injection | Outbound OAuth client secret. A client whose secret is still a blank/placeholder is **skipped** at load (`🔐 Skipped client {id}: ClientSecret is missing or empty`). |
| `AppSettings:Clients:{n}:HmacSecret` | **Yes** | ISW env injection | Per-client HMAC key (optional). |
| `AppSettings:Clients:{n}:Status` | No | `appsettings.json` | Only `ACTIVE` clients are loaded. |
| `AppSettings:Hmac:*` | No | `appsettings.json` | Shared HMAC settings (header names, algorithm, clock skew, bypass paths). |

The Vault path and policy the ISW agent uses to source these values are configured **on the agent**, not in
this application — the app is unaware of them.

---

## The send half

### Stage 1 — The loop wakes & gates on the tenant

`ResourcesFhirSyncWorker.ExecuteAsync` runs a loop (≈1-minute delay between passes). Each pass:

- **Tenant gate:** `IClientSyncConfigProvider.IsClientActiveAsync(tenantId)`. The `tenantId` is the
  **deployment identity** (`SeSClient:TenantId`) — *not* the per-record `clientId`. If the tenant is
  deactivated server-side, the worker writes a `Stopped` status and parks, polling until reactivated.
- A side task (`MonitorDeactivationAsync`) polls every few seconds and cancels mid-run if the tenant is
  deactivated.
- It processes both pools: `ProcessPoolAsync<PatientSyncRecord>` then
  `ProcessPoolAsync<GenericResourceSyncRecord>`, each under a `CorrelationId`/`RecordType` log scope, by
  calling `FhirSyncService.ProcessPendingRecordsAsync<T>`.

> Two independent notions of "client": the **tenant** (this whole deployment, gates the loop) and the
> per-record **clientId** (selects the outbound credential). They are deliberately separate.

### Stage 2 — Load pending records & keep only loadable clients

`FhirSyncService.ProcessPendingRecordsAsync<T>`:

1. `IFhirSyncStore.GetByStatusAsync<T>("Pending")` loads pending records (Mongo adapter: `Find(Status ==
   "Pending")`, default take 100).
2. **Valid-clients-only filter:** records whose `clientId` is **not** in the startup-loaded set
   (`IClientCredentialProvider.IsClientKnown`) are **skipped and left `Pending`** —
   `⏭️ Skipped N record(s) for client(s) not loaded …`. (New/rotated clients need a restart to appear.)

### Stage 3 — Fair scheduling + per-client circuit breaker

Records are grouped by `clientId` into per-client queues and drained **round-robin — one record per client
per round** — so a high-volume client can't starve others. Each client has a small **circuit breaker**:
after 3 consecutive transport/auth failures, the rest of that client's records are left `Pending` for the
next cycle (other clients are unaffected).

### Stage 4 — Per-record preparation

For each record: clean the stored payload (`FhirJson.ToCleanJson()`), detect the FHIR `resourceType`,
resolve the callback URL, and call `FhirApiService.SendAsync(Post, clientId: record.ClientId, …,
shipService: record.ShipService)`. **Credentials follow `record.ClientId`; routing follows
`record.ShipService`** — separate inputs.

### Stage 5 — Token acquisition (per-client, cached)

Inside `FhirApiService.SendAsync`:

- `FhirRoutingSettings.ResolveRoute(shipService, resourceType)` picks the target `BaseUrl` (by service name,
  else by resource type, else `Default`). **Routing only.**
- Scope comes from **`AuthSettings:Scope`** — authorization is **not** shipService-specific; the same scope
  is used for every SHIP target.
- `CachedFhirTokenService.GetAccessTokenAsync(clientId, scope)`:
  - Returns a cached token if one is valid for `(clientId, scope)` (30 s refresh margin).
  - On a miss, a per-key single-flight lock collapses concurrent demand; it resolves the credential from the
    **in-memory client set** (`IClientCredentialProvider.GetAsync`) and POSTs
    `{clientId, clientSecret, grantType, scope}` to `AuthSettings:TokenEndpoint`.
  - Caches the token by its `expires_in`. Tokens/secrets are never logged (only masked client ids).

The bearer token is attached to the request.

### Stage 6 — Build & send the HTTP request

- **Endpoint:** for `PDS`, a `Bundle` payload → `POST {baseUrl}/api/v1/Bundle`, otherwise
  `POST {baseUrl}/api/v1/{resourceType}`. Non-PDS services use `POST {baseUrl}`.
- **Envelope:** the FHIR JSON is wrapped as `{ callbackUrl, data }` — this is how SHIP learns where to
  call back with the result.
- A per-route timeout is applied, then `client.SendAsync(...)`.

### Stage 7 — Parse the synchronous response

Read the body, parse into `FhirApiResponse`. Non-success HTTP → an `error` response (not thrown).
Logged: `📬 SHIP FHIR replied HTTP 202 … {"status":"success","code":202,…,"transactionId":"txn-…"}`.

### Stage 8 — Interpret the ack & seed the StatusEvent

- A completed send (transport + auth OK) **resets the client breaker**, even if SHIP logically rejected the
  content.
- **Accepted** = `status == "success"` AND `code == 202`. On acceptance with a `transactionId`, a `PENDING`
  `StatusEvent` is inserted into `fhirstatusevents` carrying `TransactionId`, `CorrelationId`/`FacilityId`/
  `ClientId`/`ShipService`, the persisted **`EmrTargetUrl`** (= `record.ClientEMRCallbackUrl`, so the
  callback works for non-patient resources too), and probe-scheduling fields.
- This `StatusEvent` is the **join point** between the two halves.

### Stage 9 — Persist the record outcome

- **Accepted** → `RecordStatusUpdate("Synced", …, txn)`; `TimeSynced` is set on success only.
- **Not accepted** → bounded retry: increment `RetryCount` and leave `Pending` (requeue) until
  `MaxSendAttempts` (3), then mark permanently `Failed` (+ seed an `ERROR` status event).
- **Transport exception** → same bounded retry, plus the breaker counter.
- All outcomes are flushed in one bulk write; staging-DB rows are marked submitted/failed.

Result logged: `📊 Sync result for Patient: Total=1, Synced=1, Requeued=0, Failed=0`.

**At this point the send half is done:** the record is `Synced`, and a `PENDING` `StatusEvent` awaits the
real outcome.

#### Illustrative send-half log (one accepted record)

```
🔎 Pending Patient records: 1
📤 Syncing Patient record ResourceId=pat-55 (ShipService=PDS, Client=lakeshore)
📦 Wrapped FHIR payload: {"callbackUrl":"…/fhir/callback/","data":{"resourceType":"Patient","id":"pat-55"}}
📡 Sending POST https://ship-pds.local/pds/api/v1/Patient for Patient (id=pat-55) via PDS for client lakeshore
🔐 Requesting FHIR token: endpoint=…/auth/token, client_id=lake…re, scope=ship-full-access
✅ Cached FHIR token for client=lake…re scope=ship-full-access (expires_in=3600s)
📬 SHIP FHIR replied HTTP 202 … {"status":"success","code":202,…,"transactionId":"txn-7f3a91c2"}
📬 Seeded PENDING StatusEvent txn=txn-7f3a91c2 resId=pat-55
✅ Accepted ResourceId=pat-55 (Patient). Txn=txn-7f3a91c2
📊 Sync result for Patient: Total=1, Synced=1, Requeued=0, Failed=0
```

---

## The acknowledgement half

The final result is resolved one of two ways; both converge on flipping the `StatusEvent` to
`Status = "SUCCESS"`.

### Stage 10 — Receiving the acknowledgement

**Path A — async callback (happy path).** SHIP processes the resource and POSTs the result to the
`callbackUrl` sent in the envelope. That hits the **Ingestor** (the companion service), which resolves the
client from the JWT and updates the matching `fhirstatusevents` document to `Status = "SUCCESS"`. *(This
lives in the Ingestor repo; the Transmitter only consumes the resulting SUCCESS event.)*

**Path B — probe fallback.** If no callback lands within the timeout, `StatusProbeWorker`:

- `FetchDueStatusProbesAsync` finds `PENDING` events older than `StatusProbe:TimeoutSeconds`.
- `TryClaimStatusProbeAsync` atomically claims one (`ProbeStatus → InFlight`).
- Issues a **`GET`** back to SHIP via `FhirApiService` keyed by the `transactionId`.
  - `200 + SUCCESS` → `MarkProbeSuccessAndAttachPayloadAsync` flips the event to `Status = "SUCCESS"`
    (`Source = "PROBE"`) and attaches the payload.
  - `404` → stop probing (resource genuinely absent).
  - other/5xx/exception → bounded retry with backoff, then abandon.

### Stage 11 — Forwarding the result to the EMR

Once a `StatusEvent` reaches a **terminal** outcome — any of `SUCCESS`, `ERROR`, `REJECTED`, `CONFLICT`,
`DUPLICATE` (the MPI-defined set, `ShipCallbackStatus.Terminal`) — `EmrCallbackWorker` closes the loop so
the EMR receives the **final outcome, not only successes**:

- `FetchDueEmrCallbacksAsync` finds events with a **terminal** `Status` whose `CallbackStatus` is not
  `Succeeded`/`Failed` and that are due (`PENDING` is excluded — it is not an outcome);
  `TryClaimEmrCallbackAsync` claims one (`CallbackStatus → InFlight`).
- Resolves the target URL (the persisted `EmrTargetUrl`, else a patient-by-txn fallback).
- **SSRF guard** (`ICallbackUrlValidator`, opt-in via `EmrCallback:Validation:Enabled`; scheme sanity is
  always enforced).
- POSTs `{status, message, shipId, transactionId, correlationId}` with `x-transaction-id` / `x-client-id` /
  `x-correlation-id` / `x-fhir-resource-type` / `x-fhir-resource-id` headers.
  - `2xx` → `MarkEmrCallbackSucceededAsync` (terminal).
  - else → retry with exponential backoff (30 s → 1 h), dead-letter after `EmrCallback:MaxAttempts`
    (default 8) — `CallbackStatus = "Failed"`, removed from the due-poll.

#### Illustrative acknowledgement-half log (probe then callback)

```
🔎 Found 1 PENDING event(s) past timeout to probe.
➡️ Probing status for Patient/pat-55 (txn=txn-7f3a91c2, attempt=1)
📡 Sending GET https://ship-pds.local/pds/api/v1/Patient/txn-7f3a91c2 … via PDS for client lakeshore
📬 SHIP FHIR replied HTTP 200 … {"status":"SUCCESS","code":200,…}
✅ Probe success UPDATED existing StatusEvent for Patient/pat-55 (txn=txn-7f3a91c2).
🔎 Polling for due EMR callbacks…  📥 Poll complete: found=1
🚚 Dispatching EMR callback (tx=txn-7f3a91c2) → https://emr.lakeshore.local/ship/callback
✅ EMR callback delivered (tx=txn-7f3a91c2)
```

---

## State-machine summary

```
record.Status:        Pending ──send 202──▶ Synced
                          ▲   └─fail <3──┘ (requeue, RetryCount++)
                          └───fail ≥3────▶ Failed

StatusEvent.Status:   (none) ──seed──▶ PENDING ──callback OR probe──▶ SUCCESS | ERROR | REJECTED | CONFLICT | DUPLICATE  (terminal → deliver to EMR)
StatusEvent.Callback: Pending ──claim──▶ InFlight ──2xx──▶ Succeeded
                                                  └─fail ≥MaxAttempts─▶ Failed (dead-letter)
```

In steady state, a record skipped at Stage 2 (client not loaded) simply stays `Pending` and is re-evaluated
each cycle until the client is added to `AppSettings:Clients` (with its injected secret) and the worker
restarts.

---

## Known gaps

- **Non-PDS probe `GET` (Finding 4.5).** The probe resolves status with a `GET`, but `FhirApiService` only
  builds a `GET` path for **PDS** — the non-PDS branch is `POST`-only and throws `NotSupportedException`. So
  a non-PDS (e.g. SCR) record that receives **no callback** can't be probed: it retries then **abandons**,
  and its `StatusEvent` stays `PENDING`. Non-PDS status resolution therefore relies entirely on the async
  callback. See `docs/multi-client/FINDINGS.md` §4.5.

---

*Related: `docs/multi-client/SECRETS-AND-CONFIG.md` (config + ISW-injected secrets), `DEPLOYMENT.md`
(operations), `docs/multi-client/FINDINGS.md` (design findings).*
