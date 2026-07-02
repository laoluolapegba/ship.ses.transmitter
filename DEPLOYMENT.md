# SHIP SeS Transmitter — Deployment & Operations Guide

Audience: the team deploying this service for the first time. This document lists
**everything the Transmitter expects in its environment**, how configuration is loaded,
how to wire up its external dependencies, and how to read the startup logs when
something is misconfigured.

It is the companion to the **SeS Ingestor**'s `DEPLOYMENT.md`. Where the two services overlap
(secret injection, the SHIP identity service), the conventions are kept consistent so DevOps configures
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
| **ISW secret injection** (Vault agent → env vars) | Injects per-client `ClientSecret`/`HmacSecret` and other secrets into the runtime **before startup** | **Yes** (platform, out-of-process) | If a client's secret placeholder is not overridden, that client is skipped at startup and its records are left `Pending`. The app itself makes **no Vault calls**. |

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

> **Secrets are injected, not fetched.** Per-client secrets follow the same `__` config convention as
> everything else (`AppSettings__Clients__{n}__ClientSecret` / `__HmacSecret`). Their **values** are placed
> into the process environment before startup by the org's **ISW secret-injection mechanism** (a Vault
> agent/sidecar). The application never contacts Vault — it only reads its own configuration. See §3.5.

> Never put client secrets, DB passwords, or HMAC keys into `appsettings.json` (placeholders only). Use
> env vars / the ISW injector. See [`docs/multi-client/SECRETS-AND-CONFIG.md`](docs/multi-client/SECRETS-AND-CONFIG.md).

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
| `AuthSettings__ClientId` / `__ClientSecret` | No | — | **No longer used** — outbound credentials come per client from `AppSettings:Clients` (§3.5). May be left blank/removed. |

### 3.4 FHIR routing — section `FhirRouting`

Routing only (endpoint/timeout/callback) — **never credentials**. `Default` is the fallback route.

| Variable | Required | Description |
|---|---|---|
| `FhirRouting__Default__BaseUrl` | **Yes — hard-fails at startup if blank** | Fallback FHIR base URL (the gateway). |
| `FhirRouting__Default__CallbackUrlTemplate` | **Strongly recommended** | The **Ingestor ack endpoint** SHIP posts results back to — set it to the Ingestor's URL, `http://{host}/api/v1/patient/ack` (replace `{host}` with the Ingestor host; not a runtime token). It is sent with every outbound FHIR request. If blank, SHIP has nowhere to ack and delivery status is resolved **only** by the `StatusProbe` fallback (slower; logs a startup warning). |
| `FhirRouting__Apis__{n}__Name` | **Yes (per entry) — hard-fails if blank** | Service name, e.g. `PDS`, `SCR`. |
| `FhirRouting__Apis__{n}__BaseUrl` | **Yes (per entry) — hard-fails if blank** | Target base URL. |
| `FhirRouting__Apis__{n}__Resources__{m}` | No | Resource types routed to this target. |
| `FhirRouting__Apis__{n}__TimeoutSeconds` | No | Per-target timeout. |

### 3.5 Per-client credentials — section `AppSettings:Clients` (ISW-injected)

Per-client outbound credentials come from configuration: the `AppSettings:Clients` list. `appsettings.json`
ships **placeholders**; the org's ISW secret-injection mechanism (a Vault agent) writes the real values into
the process environment before startup, and the environment-variable config provider binds them over the
placeholders. The application makes **no Vault API calls**, knows no Vault address/token, and handles no
`X-Vault-Token`. At startup every `ACTIVE` client with a usable (non-placeholder) secret is loaded once into
memory (no per-request lookups, no TTL) and the loaded set is logged. **Adding, removing or rotating a client
requires a restart.**

One env-var pair per client, index-aligned to the list order in `appsettings.json`:

| Variable | Required | Default | Description |
|---|---|---|---|
| `AppSettings__Clients__{n}__ClientId` | **Yes (per entry)** | — | The client identity; **is** the outbound `client_id` and the value carried on each record. Non-secret (may stay in `appsettings.json`). |
| `AppSettings__Clients__{n}__ClientSecret` | **Yes (per entry)** | — | **Secret.** Outbound OAuth client secret. ISW-injected. |
| `AppSettings__Clients__{n}__HmacSecret` | No | — | **Secret.** Per-client HMAC key. ISW-injected. |
| `AppSettings__Clients__{n}__Status` | No | `ACTIVE` | Only `ACTIVE` clients are loaded. Non-secret. |
| `AppSettings__Hmac__*` | No | see appsettings | Shared HMAC settings (header names, algorithm, clock skew, bypass paths). Non-secret. |

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

### 4.3 Per-client secrets via ISW injection (§3.5)

The Transmitter never talks to Vault. DevOps configures the **ISW secret-injection mechanism** (the Vault
agent/sidecar that runs alongside the container) to read the approved Vault path and expose each value as an
environment variable, one pair per client, index-aligned to the `AppSettings:Clients` order:

```
AppSettings__Clients__0__ClientSecret = <ses-client-a outbound OAuth secret>
AppSettings__Clients__0__HmacSecret   = <ses-client-a HMAC key>
AppSettings__Clients__1__ClientSecret = <ses-client-b …>
AppSettings__Clients__1__HmacSecret   = <ses-client-b …>
```

The `ClientId`/`Status` and the `AppSettings:Hmac` block are non-secret and stay in `appsettings.json`; the
`ClientId` **is** the outbound `client_id` and the value carried on each record.

> **Injection, not access.** Vault remains the source of truth, but reads happen in the ISW agent —
> out-of-process. The application only reads its own configuration; it holds no Vault address, token, or
> policy, and issues no `list`/`read` calls.
>
> **Adding/rotating a client requires a restart** — the in-memory client set is built once at startup.

---

## 5. What hard-fails at startup

The worker stops at boot (rather than failing mid-run) when any of these is misconfigured:

- `FhirRouting:Default:BaseUrl` blank, or any `FhirRouting:Apis` entry missing `Name`/`BaseUrl`.
- `AuthSettings:TokenEndpoint` or `AuthSettings:Scope` blank.
- `AppSettings:ShipServerSqlDb` / `EmrDb` missing a `DbType`.
- `SeSClient:TenantId` (and legacy `ClientId`) both blank.
- Neither `FhirRouting` nor a legacy `FhirApi` block present.

**Non-fatal:** if `AppSettings:Clients` yields 0 `ACTIVE` clients with a usable secret (e.g. the ISW
injector didn't override the placeholders), the startup load logs a loud warning and loads nothing — every
record is then **skipped (left Pending)** until the config is fixed and the worker restarts. Missing
per-client secrets are a **configuration** problem, not a startup hard-fail.

**Non-fatal:** a blank `FhirRouting:Default:CallbackUrlTemplate` does **not** stop startup, but it is logged
as a loud startup warning (`⚠️ …CallbackUrlTemplate is not set …`) — SHIP then has no Ingestor ack URL and
delivery status is resolved only by the `StatusProbe` fallback. Set it to `http://{host}/api/v1/patient/ack`.

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

A healthy start logs the client-credential load result and each worker starting:

```
ClientCredentials: config provider (AppSettings:Clients), 3 ACTIVE of 3 configured. Secrets are ISW-injected via environment variables.
FeatureFlag: Using SHIP Admin API adapters (HTTP).
🔐 Client credential load complete: 3 client(s) loaded, 0 skipped. Loaded: ses-client-a, ses-client-b, ses-client-c
🌐 FHIR routing: Default BaseUrl=https://gateway/fhir, CallbackUrl (Ingestor ack)=http://ingestor.internal/api/v1/patient/ack. Routes: PDS→https://pds, SCR→https://scr
▶️ Starting Resources FHIR Sync Worker (client=lakeshore)…
🛰️ EMR Callback Worker started …
🛰️ StatusProbeWorker started …
```

- The `🌐 FHIR routing` line echoes the effective destinations and the **callback (Ingestor ack) URL**. Verify
  the `CallbackUrl` value is the Ingestor's `…/api/v1/patient/ack` — if it logs
  `⚠️ FhirRouting:Default:CallbackUrlTemplate is not set …`, SHIP cannot ack and status falls back to probing.
  Each outbound send also echoes its callback: `📡 Sending POST … CallbackUrl=…` (`CallbackUrl=<none>` if unset).
- `🔐 No ACTIVE clients with a usable secret were loaded from AppSettings:Clients …` — the ISW injector
  didn't override the placeholder secrets, or every client is non-`ACTIVE`. No records will be processed
  until fixed + restarted.
- `🔐 Skipped client {ClientId}: …` — that client wasn't loaded (non-`ACTIVE`, or its `ClientSecret` was
  still a blank/placeholder); its records stay `Pending`.

---

## 8. Pre-deployment checklist

- [ ] `SeSClient__TenantId` set to the real tenant.
- [ ] `AuthSettings__TokenEndpoint` + `__Scope` set.
- [ ] `FhirRouting__Default__BaseUrl` and each `Apis` entry's `Name`/`BaseUrl` set.
- [ ] `SourceDbSettings__ConnectionString` + `__DatabaseName` point at the real Mongo.
- [ ] `AppSettings__EmrDb__*` (and `ShipServerSqlDb__*` if `UseShipAdminApi=false`) set.
- [ ] `ShipAdminApi__BaseUrl`, `ShipAdminAuth__TokenUrl`/`__TenantId`/`__ClientSecret` set (Admin-API mode).
- [ ] `AppSettings:Clients` lists each client (`ClientId`/`Status=ACTIVE`); the ISW injector overrides every
      `AppSettings__Clients__{n}__ClientSecret` (and `__HmacSecret` where used) — no placeholders remain.
- [ ] Each `AppSettings:Clients[n].ClientId` equals the `clientId` carried on the records.

---

## 9. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| App exits at startup naming `AuthSettings`/`FhirRouting`/`AppSettings` | Required config blank | Set the named key (see §5) |
| App exits: `SeSClient:TenantId … is required` | Tenant identity missing | Set `SeSClient__TenantId` |
| Nothing transmitted; `No ACTIVE clients … loaded from AppSettings:Clients` | Placeholders not overridden by the ISW injector, or all clients non-`ACTIVE` | Fix the injected env vars / `Status`, restart |
| One client's records stay `Pending`, logged as skipped | Client not loaded (non-`ACTIVE`, secret still a placeholder, or added after startup) | Fix the client's config/injected secret + restart |
| All sends `401`/token errors | Identity endpoint wrong, or the injected per-client `ClientSecret` is wrong | Verify `AuthSettings`/the injected `AppSettings__Clients__{n}__ClientSecret` |
| Records requeue then `Failed` | SHIP FHIR endpoint unreachable or rejecting | Check `FhirRouting` base URLs / SHIP health |
| Worker self-pauses (`Client … not active`) | Admin API reports tenant inactive | Activate the tenant in SHIP Admin |
| App exits in DirectDB mode | `UseShipAdminApi=false` but `ShipServerSqlDb` misconfigured | Set the connection string or use Admin-API mode |
| EMR callbacks never delivered | Callback URL missing/blocked | Check `EmrTargetUrl`/`EmrCallback:Validation` |
| Records stay `PENDING`/only resolve via probe; no acks arrive | `FhirRouting:Default:CallbackUrlTemplate` blank or wrong — SHIP has no Ingestor ack URL | Startup logs `⚠️ …CallbackUrlTemplate is not set` and sends log `CallbackUrl=<none>`. Set it to `http://{host}/api/v1/patient/ack` (the Ingestor host) and restart |
