# Secrets & Configuration

As of 2026-05-30 the SeS Transmitter no longer ships secrets in `appsettings.json`. Secret-bearing
values were blanked and must be supplied at runtime via **environment variables** (which override
config — `Program.cs` already calls `AddEnvironmentVariables()`) or a secret store / orchestrator.

> 🚨 **Rotate the previously-committed secrets.** The DB password, `AuthSettings.ClientSecret`, and
> `ShipAdminAuth.ClientSecret` were committed in git history and must be treated as **compromised**.
> Rotate them and, if policy requires, scrub history. Blanking the current file does **not** remove
> them from past commits.

## Values now blank in `appsettings.json` (must be provided at runtime)

| Config path | Env var (note the `__` separator) | Notes |
|---|---|---|
| `AppSettings:ShipServerSqlDb:ConnectionString` | `AppSettings__ShipServerSqlDb__ConnectionString` | MySQL/PG connection string for the SHIP server DB. |
| `AppSettings:EmrDb:ConnectionString` | `AppSettings__EmrDb__ConnectionString` | Connection string for the extractor staging DB. |
| `ShipAdminAuth:ClientSecret` | `ShipAdminAuth__ClientSecret` | Tenant-level Admin API secret (stays tenant-scoped). |
| `VAULT_TOKEN` | `VAULT_TOKEN` (plain OS env var) | **Required.** Vault token for per-client outbound credentials. See the Vault section below. |
| `EmrCallback:Proxy:Auth:Username` / `Password` | `EmrCallback__Proxy__Auth__Username` / `__Password` | Already placeholder (`__from_secret_store__`); supply only when proxy auth is `Basic`. |

> `AuthSettings:ClientSecret` is **no longer used** — outbound credentials come from Vault per client.
> It may be left blank or removed from `appsettings.json`.

## Example (local development)

PowerShell:

```powershell
$env:AppSettings__ShipServerSqlDb__ConnectionString = "Host=...;Port=3306;Database=...;Username=...;Password=..."
$env:AppSettings__EmrDb__ConnectionString          = "Host=...;..."
$env:ShipAdminAuth__ClientSecret                   = "<dev-admin-secret>"
$env:VAULT_ADDR                                    = "https://vault.internal:8200"   # required (worker exits if unset)
$env:VAULT_TOKEN                                   = "<vault-token>"                 # required
dotnet run --project Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker
```

In containers, inject these as environment variables from your orchestrator's secret mechanism
(Kubernetes `Secret` → env, Docker `--env-file`, etc.). Do not bake them into images.

## What remains in config (non-secret — OK to keep)

- URLs/endpoints: `AuthSettings:TokenEndpoint`, `ShipAdminApi:BaseUrl`, `ShipAdminAuth:TokenUrl`,
  `FhirRouting:*:BaseUrl`, `SourceDbSettings:ConnectionString` (Mongo, currently no credentials),
  callback templates.
- Identifiers/scopes/tuning: `SeSClient:TenantId`, `AuthSettings:Scope` (outbound authorization scope —
  the same for every SHIP target system, no longer per FHIR route), timeouts, batch sizes, heartbeat
  intervals, resource lists.

## Per-client credentials via Vault (env-configured, Vault-only)

Outbound per-client credentials are resolved **only from Vault**, configured via **OS environment
variables** — the exact mechanism the SeS Ingestor uses, so DevOps configures Vault clients once and both
services retrieve the same way. **There is no `appsettings` section and no `Config` fallback.**
`VAULT_ADDR` and `VAULT_TOKEN` are **required — the worker exits at startup if they are unset.**

At startup the provider:

1. **Discovers** every registered client by listing the prefix (`{Mount}/metadata/{ListPrefix}` for KV v2,
   where `ListPrefix` is everything in the path template before `{clientId}` — default `ses/clients`).
   The Vault folder name **is** the `clientId` (and the outbound `client_id`).
2. **Reads** each client's secret from `{Mount}/data/{path}` (default `secret/data/ses/clients/{clientId}/hmac`).
   Non-secret material (token endpoint, grant type) comes from `AuthSettings`.
3. **Filters** to valid clients only: loaded only when **active and not revoked** (`isActive`/`isRevoked`
   booleans, or `status` = `revoked`/`inactive`) and the secret field is present.

The loaded client set, and the count skipped, are **reported in the startup log**. There are **no
per-request Vault calls and no TTL cache** — the worker processes only the clients loaded at startup;
records for unknown/inactive clients are skipped (left `Pending`). **Adding or rotating a client requires a
restart.**

The Vault token needs `list` on the prefix and `read` on the client paths, e.g.:

```hcl
path "secret/data/ses/clients/*"   { capabilities = ["read"] }
path "secret/metadata/ses/clients" { capabilities = ["list"] }
```

Environment variables (plain OS env vars, **not** the `__` config convention):

| Variable | Required | Default | Notes |
|---|---|---|---|
| `VAULT_ADDR` | **Yes — worker exits if unset** | — | e.g. `https://vault.internal:8200`. |
| `VAULT_TOKEN` | **Yes — worker exits if unset** | — | **Secret.** Needs `list` + `read` (above). Provision via Kubernetes auth / injected env. |
| `VAULT_HMAC_MOUNT` | No | `secret` | KV mount. |
| `VAULT_HMAC_KV_VERSION` | No | `2` | KV engine version (controls `data`/`metadata` segments). |
| `VAULT_HMAC_PATH_TEMPLATE` | No | `ses/clients/{clientId}/hmac` | Logical per-client path; `{clientId}` (folder name) substituted. Do **not** include `data`/`metadata`. |
| `VAULT_HMAC_SECRET_KEY` | No | `clientSecret` | Field holding the client secret. |
| `VAULT_HMAC_STATUS_KEY` | No | `status` | `revoked`/`inactive` disables the client. |
| `VAULT_HMAC_IS_ACTIVE_KEY` | No | `isActive` | `false` disables the client. |
| `VAULT_HMAC_IS_REVOKED_KEY` | No | `isRevoked` | `true` disables the client. |
| `VAULT_HMAC_REQUEST_TIMEOUT_SECONDS` | No | `10` | Vault HTTP timeout. |

> The Transmitter uses the **same env-var mechanism** as the Ingestor but its **own path prefix**
> (`ses/clients/...`, vs the Ingestor's `emr-clients/...`): the outbound OAuth client secret is distinct
> from the Ingestor's inbound HMAC key.
>
> `AuthSettings:ClientId`/`ClientSecret` are **no longer used** (Vault is the only credential source); only
> `AuthSettings:TokenEndpoint`/`GrantType`/`Scope` are read.

## Tenant vs client identity

`SeSClient:ClientId` was renamed to **`SeSClient:TenantId`** (the legacy `ClientId` key still binds as a
fallback). This value is the **tenant/deployment** identity used for Admin API, heartbeat, metrics and
sync enable/disable — it does **not** select outbound FHIR credentials (those are per-record `clientId`).
</content>
