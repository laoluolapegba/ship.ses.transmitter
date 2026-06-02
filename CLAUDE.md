# CLAUDE.md — SeS Transmitter

Guidance for Claude Code (and humans) working in this repository.

## What this service is

**SeS Transmitter** is the outbound half of the SHIP SeS integration. It reads
transformed FHIR records from a staging store, transmits them to SHIP services
(PDS, SCR, and other FHIR endpoints), tracks delivery status, and calls back into
the client's EMR with the result. It is a .NET worker host (`Ship.Ses.Transmitter.Worker`)
running a set of `BackgroundService` loops.

The companion **Ingestor** receives results from SHIP and resolves the authenticated
client from the JWT (`client_id` / `azp`). The transmitter is being moved to the same
**client-aware** model — see `docs/multi-client/`.

## Solution layout

```
Ship.Ses.Transmitter/
  src/
    Ship.Ses.Transmitter.Domain/          # Entities, FhirSyncRecord, StatusEvent, enums (no infra deps)
    Ship.Ses.Transmitter.Application/     # Interfaces (IFhirApiService, IFhirSyncService, ISyncMetricsWriter…), DTOs
    Ship.Ses.Transmitter.Infrastructure/  # Mongo + EF persistence, FHIR HTTP, token services, AdminApi clients, Settings
    Ship.Ses.Transmitter.Service/
      Ship.Ses.Transmitter.Worker/        # Host: Program.cs + BackgroundServices + appsettings.json
  tests/
    Ship.Ses.Transmitter.Domain.UnitTests/         # (currently empty shells — see build plan)
    Ship.Ses.Transmitter.Application.UnitTests/
    Ship.Ses.Transmitter.Infrastructure.UnitTests/
```

Clean-ish architecture: `Domain` ← `Application` ← `Infrastructure` ← `Worker`. Persistence is
abstracted by `IFhirSyncStore` (Application); the MongoDB adapter (`MongoSyncRepository`) keeps all
Mongo types internal.

## Background workers (registered in `Worker/Program.cs`)

| Worker | Responsibility |
|---|---|
| `ResourcesFhirSyncWorker` | Main loop: pulls `Pending` records (`PatientSyncRecord`, `GenericResourceSyncRecord`) and sends them via `IFhirSyncService`. Self-enables/disables based on `IClientSyncConfigProvider.IsClientActiveAsync`. |
| `EmrCallbackWorker` | Polls `fhirstatusevents` for `SUCCESS` events with pending EMR callbacks and POSTs them to the client's EMR callback URL. |
| `StatusProbeWorker` | For records that got no callback within a timeout, probes SHIP (`GET`) to resolve final status. |
| `ClientHeartbeatWorker` | Tenant-level heartbeat to the SHIP Admin API. |
| `MetricsSyncReporterWorker` | Flushes sync metrics to the Admin API. |

## Key types

- `Domain/Patients/FhirSyncRecord` (abstract) — base sync record; carries `ClientId`,
  `FacilityId`, `ShipService`, `CorrelationId`, `TransactionId`, `Status`, `RetryCount`,
  `ApiResponsePayload`, `TimeSynced`, `CollectionName`. Concrete: `PatientSyncRecord`,
  `GenericResourceSyncRecord` (collection `transformed_pool_resources`, covers all non-patient
  resource types via `[FhirResource(...)]` attributes).
- `Domain/Sync/StatusEvent` — Mongo doc in `fhirstatusevents`; drives probe + callback state machines.
- `Application/Interfaces/IFhirApiService.SendAsync` — outbound FHIR call (takes `clientId`).
- `Application/Interfaces/IFhirSyncStore` — storage-neutral persistence (Mongo adapter today).
- `Infrastructure/ReadServices/FhirSyncService` — orchestrates pending-record processing.
- `Infrastructure/ReadServices/TokenService` / `Security/AdminTokenService` — outbound token acquisition.
- `Infrastructure/Settings/FhirRoutingSettings` — **routing-only** target resolution (`PDS`/`SCR`/Default).

## Configuration (`Worker/appsettings.json`)

- `SeSClient.TenantId` — tenant/deployment identity for Admin API, heartbeat, metrics, sync
  enable/disable (legacy `ClientId` key still binds as a fallback). **Not** used to select outbound
  FHIR credentials — those are resolved per-record by `clientId`.
- `ClientCredentials.Source` — `Config` (single-client fallback via `AuthSettings`) or `Vault`
  (per-`clientId` secret from `secret/ses/clients/{clientId}`). See `docs/multi-client/SECRETS-AND-CONFIG.md`.
- `AuthSettings` — non-secret outbound FHIR defaults (token endpoint + grant type) and the `Config`
  fallback credential. Outbound tokens are acquired per-`clientId` via `IFhirTokenService` (cached per
  `(clientId, scope)`); scope is route-derived.
- `FhirRouting` — per-target `BaseUrl`/`Scope`/`ClientCert`/resource lists (routing).
- `ShipAdminApi` / `ShipAdminAuth` — tenant-level Admin API + heartbeat/metrics auth.
- `StatusProbe`, `EmrCallback` — worker tuning.
- `SourceDbSettings` — MongoDB (sync records + status events). `AppSettings.*` — MySQL/PG staging via EF.

> ⚠️ **Secrets are currently committed in plaintext** in `appsettings.json` (client secret,
> DB passwords). Do not add more. The multi-client work moves these to Vault. Treat the
> committed values as compromised / to-be-rotated.

## Data stores

- **MongoDB** (`SourceDbSettings`) — `transformed_pool_patients`, `transformed_pool_resources`,
  `fhirstatusevents`. Accessed via `MongoSyncRepository : IMongoSyncRepository`.
- **MySQL/Postgres/SqlServer** (EF, `AppSettings.ShipServerSqlDb` + `EmrDb`) — client sync config
  (when `UseShipAdminApi=false`) and the extractor staging DB updates.

Persistence is accessed through the storage-neutral **`IFhirSyncStore`** (Application) — string ids,
JSON-string payloads, no `ObjectId`/`BsonDocument`. `MongoSyncRepository` is the MongoDB adapter; a
future PostgreSQL migration is a drop-in adapter implementing the same interface (use
`SELECT … FOR UPDATE SKIP LOCKED` for the `TryClaim…` methods). Do **not** reintroduce Mongo types into
the interface or call the adapter directly from workers/services.

## Build / test

```powershell
# from repo root
dotnet build Ship.Ses.Transmitter/Ship.Ses.Transmitter.sln
dotnet test  Ship.Ses.Transmitter/Ship.Ses.Transmitter.sln
```

- Target frameworks: libraries/tests `net9.0`; the Worker has both `net8.0`/`net9.0` obj output —
  confirm the intended TFM before changing.
- Test projects use **xUnit** + `coverlet.collector`. They exist in the solution but contain no
  tests yet (see `docs/multi-client/BUILD-PLAN.md`).

## Conventions

- Workers are `BackgroundService` loops with structured Serilog logging (emoji prefixes are the
  house style; keep them consistent if you touch a log line).
- Use `LogContext.PushProperty` for `CorrelationId`/`RecordType` scoping.
- **Never log token material** (not even prefixes) and never log secrets.
- Carry `clientId`, `facilityId`, `correlationId`, `transactionId`, `shipService` through processing
  and into `StatusEvent` — these are the audit/trace dimensions.
- `shipService`/`targetSystem` selects **routing only**, never credentials.

## Active initiative: multi-client awareness

The transmitter must stop assuming one client. Read before making related changes:

- `docs/multi-client/FINDINGS.md` — full inspection of single-client assumptions.
- `docs/multi-client/BUILD-PLAN.md` — phased implementation plan (incl. test projects).

Design rules to honor:
1. The same client credential is used across all SHIP target systems (PDS/SCR/…).
2. Credential resolution is **per `clientId` only**, not per target system.
3. `targetSystem`/`shipService` is routing/processing only — never credential selection.
4. Client-specific secrets must not live in instance `appsettings.json`.
5. Client secrets resolve dynamically from Vault: `secret/ses/clients/{clientId}`.
6. Non-secret settings may remain in configuration.
</content>
