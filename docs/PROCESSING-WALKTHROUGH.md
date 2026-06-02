# SeS Transmitter — End-to-End Processing Walkthrough

How a single transformed FHIR record travels through the Transmitter: from being picked up in the
staging store, authenticated and sent to SHIP, through to the acknowledgement being delivered back to
the client's EMR. This is the outbound half of the SHIP SeS integration.

> **Key idea up front — the Transmitter never blocks waiting for SHIP's final result.**
> SHIP answers the outbound `POST` synchronously with **`202 Accepted` + a `transactionId`** — that's
> the "I've queued it" acknowledgement, **not** the final clinical outcome. The real result arrives
> later, asynchronously: either SHIP calls back (received by the companion **Ingestor**, which writes a
> `SUCCESS` status event) or the Transmitter's **probe** worker pulls it. So the flow has two halves:
>
> - **Send half** — `ResourcesFhirSyncWorker → FhirSyncService → FhirApiService` (Stages 1–9).
> - **Acknowledgement half** — the `StatusEvent` state machine, `StatusProbeWorker`, `EmrCallbackWorker`
>   (Stages 10–11).

---

## Contents

- [Components](#components)
- [Stage 0 — Startup: load the client credentials from Vault](#stage-0--startup-load-the-client-credentials-from-vault)
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
| `Infrastructure/Security/VaultClientCredentialProvider` | Loads per-client secrets from Vault at startup; resolves them from memory. |
| `Application/Interfaces/IFhirSyncStore` (Mongo adapter `MongoSyncRepository`) | Storage-neutral persistence for records + status events. |
| `Worker/StatusProbeWorker` | Probes SHIP for records that got no callback within a timeout. |
| `Worker/EmrCallbackWorker` | Delivers `SUCCESS` results back to the client's EMR callback URL. |

Two record pools live in MongoDB: `PatientSyncRecord` (`transformed_pool_patients`) and
`GenericResourceSyncRecord` (`transformed_pool_resources`, all non-patient resource types). Status
events live in `fhirstatusevents`.

---

## Stage 0 — Startup: load the client credentials from Vault

Before any worker processes a record, `Program.cs` calls `IClientCredentialProvider.InitializeAsync()`
once. Outbound credentials come **only from Vault**, configured via **OS environment variables** (the same
mechanism the Ingestor uses). The startup sequence:

1. **Connect** using `VAULT_ADDR` + `VAULT_TOKEN`. **If either is unset the worker exits at startup** —
   Vault is mandatory.
2. **Discover** the registered clients: list the prefix
   `GET {VAULT_ADDR}/v1/{VAULT_MOUNT}/metadata/{prefix}?list=true` (KV v2), where `prefix` is everything in
   `VAULT_PATH_TEMPLATE` *before* `{clientId}` (default `ses/clients`). The returned folder names **are**
   the clientIds.
3. **Read each** client's secret at `{VAULT_MOUNT}/data/{VAULT_PATH_TEMPLATE}` with `{clientId}`
   substituted, e.g. `secret/data/ses/clients/lakeshore`.
4. **Load into memory** as `ClientCredential(TokenEndpoint, clientId, clientSecret, GrantType)` — the
   `clientId` and `clientSecret` come from Vault; the `TokenEndpoint`/`GrantType` come from `AuthSettings`.
5. **Report** the result in the log:
   `🔐 Vault credential load complete: 3 client(s) loaded, 0 skipped (of 3 discovered). Loaded: …`.

There are **no per-request Vault calls and no TTL** — every credential is resolved from this in-memory set
thereafter. **Only loaded clients are processed** (Stage 2). Adding, removing or rotating a client requires
a **restart**.

### Vault environment variables

Only the first two are required; the rest are override knobs with working defaults.

| Variable | Required | Default | Meaning |
|---|---|---|---|
| `VAULT_ADDR` | **Yes (worker exits if unset)** | — | Vault base URL, e.g. `https://vault.internal:8200`. |
| `VAULT_TOKEN` | **Yes (worker exits if unset)** | — | Vault token; needs `list` on the prefix + `read` on the client paths. |
| `VAULT_MOUNT` | No | `secret` | KV mount point. |
| `VAULT_KV_VERSION` | No | `2` | KV engine version (controls the `data`/`metadata` path segments). |
| `VAULT_PATH_TEMPLATE` | No | `ses/clients/{clientId}` | Logical per-client path; `{clientId}` (folder name) substituted. |
| `VAULT_SECRET_KEY` | No | `clientSecret` | **Which field inside the secret holds the client secret** (see below). |
| `VAULT_REQUEST_TIMEOUT_SECONDS` | No | `10` | Vault HTTP timeout. |

### What `VAULT_SECRET_KEY` is (and why you usually don't set it)

A Vault KV secret is a **JSON object — a map of fields**, not a single string. So
`secret/ses/clients/lakeshore` holds something like `{ "clientSecret": "abc123" }`. `VAULT_SECRET_KEY`
names **which field** is the outbound OAuth client secret:

```
secret      = read("secret/data/ses/clients/lakeshore")   // a dictionary of fields
clientSecret = secret[VAULT_SECRET_KEY]                     // pick the field (default "clientSecret")
```

It defaults to `clientSecret`, which matches the documented way of writing an entry:

```bash
vault kv put secret/ses/clients/lakeshore clientSecret="<outbound-oauth-client-secret>"
```

So you only set `VAULT_SECRET_KEY` if your entries store the secret under a different field name. A client
folder that exists but whose secret is under a different/missing field is **skipped** at load (logged:
`skipped client {id}: secret field 'clientSecret' is missing or empty`) — the usual reason an expected
client isn't processed.

The Vault token policy:

```hcl
path "secret/data/ses/clients/*"   { capabilities = ["read"] }
path "secret/metadata/ses/clients" { capabilities = ["list"] }
```

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
> per-record **clientId** (selects the Vault credential). They are deliberately separate.

### Stage 2 — Load pending records & keep only loadable clients

`FhirSyncService.ProcessPendingRecordsAsync<T>`:

1. `IFhirSyncStore.GetByStatusAsync<T>("Pending")` loads pending records (Mongo adapter: `Find(Status ==
   "Pending")`, default take 100).
2. **Valid-clients-only filter:** records whose `clientId` is **not** in the Vault-loaded set
   (`IClientCredentialProvider.IsClientKnown`) are **skipped and left `Pending`** —
   `⏭️ Skipped N record(s) for client(s) not loaded from Vault …`. (New/rotated clients need a restart to
   appear.)

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
    **in-memory Vault set** (`IClientCredentialProvider.GetAsync` — no Vault call) and POSTs
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

Once a `StatusEvent` is `Status = "SUCCESS"`, `EmrCallbackWorker` closes the loop:

- `FetchDueEmrCallbacksAsync` finds `SUCCESS` events whose `CallbackStatus` is not `Succeeded`/`Failed` and
  that are due; `TryClaimEmrCallbackAsync` claims one (`CallbackStatus → InFlight`).
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

StatusEvent.Status:   (none) ──seed──▶ PENDING ──callback OR probe──▶ SUCCESS
StatusEvent.Callback: Pending ──claim──▶ InFlight ──2xx──▶ Succeeded
                                                  └─fail ≥MaxAttempts─▶ Failed (dead-letter)
```

In steady state, a record skipped at Stage 2 (client not loaded) simply stays `Pending` and is re-evaluated
each cycle until the client is added in Vault and the worker restarts.

---

## Known gaps

- **Non-PDS probe `GET` (Finding 4.5).** The probe resolves status with a `GET`, but `FhirApiService` only
  builds a `GET` path for **PDS** — the non-PDS branch is `POST`-only and throws `NotSupportedException`. So
  a non-PDS (e.g. SCR) record that receives **no callback** can't be probed: it retries then **abandons**,
  and its `StatusEvent` stays `PENDING`. Non-PDS status resolution therefore relies entirely on the async
  callback. See `docs/multi-client/FINDINGS.md` §4.5.

---

*Related: `docs/multi-client/SECRETS-AND-CONFIG.md` (Vault env vars + secrets), `DEPLOYMENT.md`
(operations), `docs/multi-client/FINDINGS.md` (design findings).*
