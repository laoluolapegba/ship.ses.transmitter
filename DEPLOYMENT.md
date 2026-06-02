# SHIP SeS Transmitter — Deployment & Operations Guide

Audience: the team deploying this service for the first time. This document lists
**everything the Transmitter expects in its environment**, how configuration is loaded,
how to wire up its external dependencies, and how to read the startup logs when
something is misconfigured.

It is the companion to the **SeS Ingestor**'s `DEPLOYMENT.md`. Where the two services overlap
(Vault, the SHIP identity service), the conventions are kept consistent so DevOps configures
each dependency once — differences are called out explicitly below.

---

## 1. What it is and what it depends on

- **Runtime:** .NET 9 **Worker Service** (`Microsoft.NET.Sdk.Worker`), designed to run as a Linux
  container in Kubernetes. It is a set of `BackgroundService` loops — **there is no HTTP endpoint and
  no `/health` probe.** Use a process/liveness check (the container staying up) and the startup logs.
- **Entry assembly:** `Ship.Ses.Transmitter.Worker.dll`.
- **External dependencies:**

| Dependency | Used for | Required? | Failure symptom |
|---|---|---|---|
| **MongoDB** | Sync records (`transformed_pool_*`) + status events (`fhirstatusevents`) | **Yes** | Workers throw on first DB access; nothing is transmitted. |
| **SHIP Identity service** | Outbound FHIR bearer tokens (per client) + Admin API token | **Yes** | All sends/heartbeats fail auth. |
| **SHIP FHIR endpoints** (gateway / PDS / SCR) | Destinations for transmitted records | **Yes** | Sends fail; records requeue then permanently fail after the attempt cap. |
| **EMR staging DB** (MySQL/PostgreSQL/SqlServer, EF) | Marking staged rows submitted/failed | **Yes** | App fails to start if `AppSettings:EmrDb` is misconfigured. |
| **SHIP Admin API** | Tenant heartbeat, metrics, sync enable/disable | Only when `SeSClient:UseShipAdminApi=true` (default) | Heartbeat/metrics fail; worker self-pauses if it can't confirm the tenant is active. |
| **SHIP server DB** (EF) | Client sync config when **not** using the Admin API | Only when `SeSClient:UseShipAdminApi=false` | App fails to start (DbContext) in DirectDB mode. |
| **HashiCorp Vault** | Per-client outbound credentials, loaded once at startup | **Yes** (env-configured) | Worker **exits at startup** if `VAULT_ADDR`/`VAULT_TOKEN` unset; if reachable but 0 clients, every record is skipped (left `Pending`). |

---

## 2. How configuration is loaded

Configuration is layered; later sources override earlier ones:

1. **`appsettings.json`** — baked into the image. Holds **non-secret defaults only** (routing URLs,
   timeouts, batch sizes, log config). Secret-bearing values are **blank** and must be supplied at runtime.
2. **`appsettings.{ASPNETCORE_ENVIRONMENT}.json`** — optional per-environment overrides.
3. **Environment variables** — the primary mechanism for deployment. Use these for everything
   environment-specific and **all secrets** (`Program.cs` calls `AddEnvironmentVariables()`).

### Env-var naming convention

.NET configuration keys use a **double underscore `__`** as the section separator:
`AuthSettings:ClientSecret` → `AuthSettings__ClientSecret`.

> **Vault is configured exactly like the Ingestor:** plain OS env vars (`VAULT_ADDR`, `VAULT_TOKEN`,
> `VAULT_HMAC_*`) — **not** the `__` config convention, and there is **no `appsettings` section**. The only
> difference is the path prefix (`ses/clients/...` vs the Ingestor's `emr-clients/...`). `VAULT_ADDR`/`VAULT_TOKEN`
> are required — the worker **exits at startup** if they are unset. See §3.5.

> Never put client secrets, DB passwords, or the Vault token into `appsettings.json`. Use env vars /
> Kubernetes Secrets. See [`docs/multi-client/SECRETS-AND-CONFIG.md`](docs/multi-client/SECRETS-AND-CONFIG.md).

---

## 3. Environment variable reference

Legend: **Required** = the service will not function (or hard-fails at startup, noted) without it.

### 3.1 .NET runtime / hosting

| Variable | Required | Default | Description |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | No | `Production` | Selects `appsettings.{Environment}.json`. |
| `OpenTelemetry__OtlpEndpoint` | No | `http://localhost:4317` | OTLP traces/metrics exporter endpoint. |

### 3.2 Identity / deployment — section `SeSClient`

| Variable | Required | Default | Description |
|---|---|---|---|
| `SeSClient__TenantId` | **Yes — worker throws at startup if missing** | `lakeshore` (dev) | Tenant/deployment identity for Admin API, heartbeat, metrics, sync enable/disable. **Not** an outbound FHIR credential. Legacy `SeSClient__ClientId` still binds as a fallback. |
| `SeSClient__UseShipAdminApi` | No | `true` | `true` = SHIP Admin API adapters (HTTP). `false` = direct EF/DB adapters (needs `AppSettings:ShipServerSqlDb`). |
| `SeSClient__HeartbeatSeconds` | No | `90` | Tenant heartbeat interval. |
| `SeSClient__MetricsFlushSeconds` | No | `300` | Metrics flush interval. |

### 3.3 Outbound FHIR authorization — section `AuthSettings`

Non-secret defaults for outbound tokens. **Scope is here, not per FHIR route** — the same credential and
scope authenticate to every SHIP target system (PDS, SCR, …).

| Variable | Required | Default (appsettings) | Description |
|---|---|---|---|
| `AuthSettings__TokenEndpoint` | **Yes — hard-fails at startup if blank** | identity URL | SHIP identity token endpoint (used per client). |
| `AuthSettings__Scope` | **Yes — hard-fails at startup if blank** | `ship-full-access` | Outbound authorization scope (every target). |
| `AuthSettings__GrantType` | No | `client_credentials` | OAuth grant type. |
| `AuthSettings__ClientId` / `__ClientSecret` | No | — | **No longer used** — outbound credentials come from Vault per client (§3.5). May be left blank/removed. |

### 3.4 FHIR routing — section `FhirRouting`

Routing only (endpoint/timeout/callback) — **never credentials**. `Default` is the fallback route.

| Variable | Required | Description |
|---|---|---|
| `FhirRouting__Default__BaseUrl` | **Yes — hard-fails at startup if blank** | Fallback FHIR base URL (the gateway). |
| `FhirRouting__Default__CallbackUrlTemplate` | Recommended | URL SHIP calls back with results. |
| `FhirRouting__Apis__{n}__Name` | **Yes (per entry) — hard-fails if blank** | Service name, e.g. `PDS`, `SCR`. |
| `FhirRouting__Apis__{n}__BaseUrl` | **Yes (per entry) — hard-fails if blank** | Target base URL. |
| `FhirRouting__Apis__{n}__Resources__{m}` | No | Resource types routed to this target. |
| `FhirRouting__Apis__{n}__TimeoutSeconds` | No | Per-target timeout. |

### 3.5 Per-client credentials (Vault) — plain OS env vars

Per-client outbound credentials come **only from Vault**, configured via **plain OS env vars** (not the `__`
convention, no `appsettings` section) — same as the Ingestor. Every client is discovered (by listing the
prefix) and read **once at startup** into memory (no per-request calls, no TTL); **only active, non-revoked**
clients are loaded, and the loaded set is logged. **Adding or rotating a client requires a restart.** The
Vault token needs `list` on the prefix and `read` on the client paths. There is **no `Config` fallback** —
`VAULT_ADDR`/`VAULT_TOKEN` are required and the worker **exits at startup** if either is unset.

| Variable | Required | Default | Description |
|---|---|---|---|
| `VAULT_ADDR` | **Yes — worker exits if unset** | — | Vault base URL, e.g. `https://vault.internal:8200`. |
| `VAULT_TOKEN` | **Yes — worker exits if unset** | — | Vault token. **Secret.** Needs `list` + `read` (see §4.3). |
| `VAULT_HMAC_MOUNT` | No | `secret` | KV mount point. |
| `VAULT_HMAC_KV_VERSION` | No | `2` | KV engine version (controls `data`/`metadata` segments). |
| `VAULT_HMAC_PATH_TEMPLATE` | No | `ses/clients/{clientId}/hmac` | **Logical** per-client path; `{clientId}` (folder name) substituted. Do **not** include `data`/`metadata`. |
| `VAULT_HMAC_SECRET_KEY` | No | `clientSecret` | Field holding the client secret. |
| `VAULT_HMAC_STATUS_KEY` | No | `status` | `revoked`/`inactive` disables the client. |
| `VAULT_HMAC_IS_ACTIVE_KEY` | No | `isActive` | `false` disables the client. |
| `VAULT_HMAC_IS_REVOKED_KEY` | No | `isRevoked` | `true` disables the client. |
| `VAULT_HMAC_REQUEST_TIMEOUT_SECONDS` | No | `10` | Vault HTTP timeout. |

### 3.6 MongoDB — section `SourceDbSettings`

| Variable | Required | Default | Description |
|---|---|---|---|
| `SourceDbSettings__ConnectionString` | **Yes** | `mongodb://localhost:27017` | Mongo connection string. |
| `SourceDbSettings__DatabaseName` | **Yes** | `shipses` | Database for sync records + status events. |

### 3.7 Staging / server DBs — section `AppSettings`

| Variable | Required | Description |
|---|---|---|
| `AppSettings__EmrDb__ConnectionString` | **Yes** | Extractor staging DB connection string. |
| `AppSettings__EmrDb__DbType` | **Yes — startup validation** | `MySql` / `Postgres` / `SqlServer`. |
| `AppSettings__ShipServerSqlDb__ConnectionString` | Only when `UseShipAdminApi=false` | SHIP server DB connection string. |
| `AppSettings__ShipServerSqlDb__DbType` | **Yes — startup validation** | DB provider (validated even in Admin-API mode). |

### 3.8 Admin API + tenant auth — sections `ShipAdminApi`, `ShipAdminAuth`

| Variable | Required | Default | Description |
|---|---|---|---|
| `ShipAdminApi__BaseUrl` | When `UseShipAdminApi=true` | — | Admin API base URL. |
| `ShipAdminAuth__TokenUrl` | When `UseShipAdminApi=true` | — | Tenant token endpoint. |
| `ShipAdminAuth__TenantId` | When `UseShipAdminApi=true` | `lakeshore` | Tenant identity for Admin API auth (sent on the wire as the `clientId` token param). Was `ClientId`. |
| `ShipAdminAuth__ClientSecret` | When `UseShipAdminApi=true` | *(blank)* | **Secret.** Tenant Admin API secret. |
| `ShipAdminAuth__Scope` | No | `ship-full-access` | Admin API token scope. |

### 3.9 Worker tuning — sections `StatusProbe`, `EmrCallback`

| Variable | Required | Default | Description |
|---|---|---|---|
| `StatusProbe__Enabled` | No | `true` | Enables the status-probe fallback. |
| `StatusProbe__TimeoutSeconds` | No | `120` | How long after seeding a PENDING event before probing SHIP. |
| `EmrCallback__MaxAttempts` | No | `8` | Callback delivery attempts before dead-lettering. |
| `EmrCallback__Validation__Enabled` | No | `false` | SSRF allow-list enforcement for EMR callback URLs (opt-in). |

### 3.10 Logging (Serilog)

Serilog writes JSON to **Console** and a rolling file at **`logs/log.txt`** (relative to the working dir).
In a container, collect stdout (recommended) or mount a writable `logs/` volume.

---

## 4. External dependency setup

### 4.1 MongoDB
Provision a database and a read/write user. Supply `SourceDbSettings__ConnectionString` + `__DatabaseName`.

### 4.2 SHIP identity / FHIR endpoints
Set `AuthSettings__TokenEndpoint` to the identity token endpoint and the `FhirRouting` base URLs to the
gateway/PDS/SCR endpoints. Outbound tokens are acquired per client and cached per `(clientId, scope)`.

### 4.3 Vault — per-client outbound credentials (env-configured; §3.5)

The Transmitter reads all client secrets **once at startup** (mirroring the Ingestor), configured via the
`VAULT_*` env vars in §3.5. The **folder name is the ClientId** (the value carried on each record) and the
secret holds the outbound OAuth `clientSecret`.

**Store each client (KV v2):**
```bash
# CLI hides the "data" segment; this writes to secret/data/ses/clients/lakeshore/hmac
vault kv put secret/ses/clients/lakeshore/hmac \
    clientSecret="<outbound-oauth-client-secret>" \
    isActive=true
```

> **Same mechanism as the Ingestor, own path.** The Transmitter's prefix is `ses/clients/...` and the secret
> is the **outbound OAuth client secret**; the Ingestor's `emr-clients/...` secret is the **inbound HMAC key**.
> They are distinct credentials at distinct paths — configure both per client.

**Policy the token needs:**
```hcl
path "secret/data/ses/clients/*"   { capabilities = ["read"] }
path "secret/metadata/ses/clients" { capabilities = ["list"] }
```

> **Adding/rotating a client requires a restart** — the in-memory client set is built once at startup.
> There are no per-request Vault calls and no TTL refresh.

---

## 5. What hard-fails at startup

The worker stops at boot (rather than failing mid-run) when any of these is misconfigured:

- `VAULT_ADDR` or `VAULT_TOKEN` unset (Vault is the only outbound-credential source).
- `FhirRouting:Default:BaseUrl` blank, or any `FhirRouting:Apis` entry missing `Name`/`BaseUrl`.
- `AuthSettings:TokenEndpoint` or `AuthSettings:Scope` blank.
- `AppSettings:ShipServerSqlDb` / `EmrDb` missing a `DbType`.
- `SeSClient:TenantId` (and legacy `ClientId`) both blank.
- Neither `FhirRouting` nor a legacy `FhirApi` block present.

**Non-fatal:** if Vault is **reachable** but returns 0 clients (or the token lacks `list`/`read`), the
startup load logs a loud warning and loads nothing — every record is then **skipped (left Pending)** until
the issue is fixed and the worker restarts. (Missing `VAULT_ADDR`/`VAULT_TOKEN`, by contrast, hard-fails.)

---

## 6. Background workers

| Worker | Responsibility |
|---|---|
| `ResourcesFhirSyncWorker` | Main loop: pulls `Pending` records and transmits them. Self-pauses if the tenant is inactive. |
| `EmrCallbackWorker` | Delivers `SUCCESS` status events back to the client's EMR callback URL. |
| `StatusProbeWorker` | Probes SHIP for records that got no callback within the timeout. |
| `ClientHeartbeatWorker` | Tenant heartbeat to the SHIP Admin API. |
| `MetricsSyncReporterWorker` | Flushes sync metrics to the Admin API. |

---

## 7. Startup logs to verify a good deployment

A healthy start logs the Vault endpoint, the load result, and each worker starting:

```
ClientCredentials: Vault provider (env-configured) at https://vault.internal:8200, prefix 'ses/clients'.
FeatureFlag: Using SHIP Admin API adapters (HTTP).
🔐 Vault credential load: discovering clients at https://vault.internal:8200 (mount 'secret', KV v2) under prefix 'ses/clients'…
🔐 Vault credential load complete: 3 active client(s) loaded, 0 skipped (of 3 discovered). Loaded: lakeshore, emr-b, emr-c
▶️ Starting Resources FHIR Sync Worker (client=lakeshore)…
🛰️ EMR Callback Worker started …
🛰️ StatusProbeWorker started …
```

- App **exits immediately** with the `Vault is not configured` message if `VAULT_ADDR`/`VAULT_TOKEN` are unset.
- `🔐 Vault credential load found no registered clients …` — Vault reachable but empty, or the token lacks
  `list`/`read`. No records will be processed until fixed + restarted.
- `⏭️ Skipped N … record(s) for unknown/inactive client(s) …` — those clients aren't loaded (inactive,
  revoked, or added after startup); their records stay `Pending`.

---

## 8. Pre-deployment checklist

- [ ] `SeSClient__TenantId` set to the real tenant.
- [ ] `AuthSettings__TokenEndpoint` + `__Scope` set.
- [ ] `FhirRouting__Default__BaseUrl` and each `Apis` entry's `Name`/`BaseUrl` set.
- [ ] `SourceDbSettings__ConnectionString` + `__DatabaseName` point at the real Mongo.
- [ ] `AppSettings__EmrDb__*` (and `ShipServerSqlDb__*` if `UseShipAdminApi=false`) set.
- [ ] `ShipAdminApi__BaseUrl`, `ShipAdminAuth__TokenUrl`/`__TenantId`/`__ClientSecret` set (Admin-API mode).
- [ ] `VAULT_ADDR` + `VAULT_TOKEN` set (worker exits otherwise); token has `list` + `read` (§4.3);
      at least one active client secret exists under `ses/clients/`.
- [ ] Each Vault client folder name equals the `clientId` carried on the records.

---

## 9. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| App exits at startup naming `AuthSettings`/`FhirRouting`/`AppSettings` | Required config blank | Set the named key (see §5) |
| App exits: `SeSClient:TenantId … is required` | Tenant identity missing | Set `SeSClient__TenantId` |
| Nothing transmitted; `Vault … found no registered clients` | Vault empty or token lacks `list`/`read` | Add secrets / fix policy / token, restart |
| One client's records stay `Pending`, logged as skipped | Client inactive/revoked, or added after startup | Activate in Vault / restart to load it |
| All sends `401`/token errors | Identity endpoint/secret wrong, or per-client secret missing in Vault | Verify `AuthSettings`/Vault secret |
| Records requeue then `Failed` | SHIP FHIR endpoint unreachable or rejecting | Check `FhirRouting` base URLs / SHIP health |
| Worker self-pauses (`Client … not active`) | Admin API reports tenant inactive | Activate the tenant in SHIP Admin |
| App exits in DirectDB mode | `UseShipAdminApi=false` but `ShipServerSqlDb` misconfigured | Set the connection string or use Admin-API mode |
| EMR callbacks never delivered | Callback URL missing/blocked | Check `EmrTargetUrl`/`EmrCallback:Validation` |
