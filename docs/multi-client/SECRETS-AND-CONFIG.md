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
| `AuthSettings:ClientSecret` | `AuthSettings__ClientSecret` | Outbound FHIR client secret. **Interim** — becomes per-`clientId` Vault lookup (Build Plan Phase 4). |
| `ShipAdminAuth:ClientSecret` | `ShipAdminAuth__ClientSecret` | Tenant-level Admin API secret (stays tenant-scoped). |
| `EmrCallback:Proxy:Auth:Username` / `Password` | `EmrCallback__Proxy__Auth__Username` / `__Password` | Already placeholder (`__from_secret_store__`); supply only when proxy auth is `Basic`. |

## Example (local development)

PowerShell:

```powershell
$env:AppSettings__ShipServerSqlDb__ConnectionString = "Host=...;Port=3306;Database=...;Username=...;Password=..."
$env:AppSettings__EmrDb__ConnectionString          = "Host=...;..."
$env:AuthSettings__ClientSecret                    = "<dev-secret>"
$env:ShipAdminAuth__ClientSecret                   = "<dev-admin-secret>"
dotnet run --project Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker
```

In containers, inject these as environment variables from your orchestrator's secret mechanism
(Kubernetes `Secret` → env, Docker `--env-file`, etc.). Do not bake them into images.

## What remains in config (non-secret — OK to keep)

- URLs/endpoints: `AuthSettings:TokenEndpoint`, `ShipAdminApi:BaseUrl`, `ShipAdminAuth:TokenUrl`,
  `FhirRouting:*:BaseUrl`, `SourceDbSettings:ConnectionString` (Mongo, currently no credentials),
  callback templates.
- Identifiers/scopes/tuning: `SeSClient:ClientId`, `*:Scope`, timeouts, batch sizes, heartbeat
  intervals, resource lists.

## Per-client credentials via Vault (Build Plan Phase 4 — implemented)

Credential source is feature-flagged by `ClientCredentials:Source`:

- **`Config`** (default) — single-client fallback: every `clientId` resolves to the `AuthSettings`
  credential. Keeps current behaviour; no Vault required.
- **`Vault`** — per-client: the client secret/HMAC is read from Vault KV v2 at
  `{KvMount}/data/{PathTemplate}` (default `secret/data/ses/clients/{clientId}/hmac`), keyed by the
  `clientId` stored on each record. Non-secret material (token endpoint, grant type) still comes from
  `AuthSettings`. Resolved credentials are cached per client for `CacheTtlSeconds` (rotation is picked
  up within the TTL; `Invalidate(clientId)` clears eagerly).

`ClientCredentials` config (non-secret except the Vault token):

| Path | Env var | Notes |
|---|---|---|
| `ClientCredentials:Source` | `ClientCredentials__Source` | `Config` or `Vault`. |
| `ClientCredentials:Vault:Address` | `ClientCredentials__Vault__Address` | e.g. `https://vault.internal:8200`. |
| `ClientCredentials:Vault:Token` | `ClientCredentials__Vault__Token` | **Secret** — supply via env/secret store, never commit. |
| `ClientCredentials:Vault:KvMount` | `…__Vault__KvMount` | KV v2 mount (default `secret`). |
| `ClientCredentials:Vault:PathTemplate` | `…__Vault__PathTemplate` | default `ses/clients/{clientId}/hmac`. |
| `ClientCredentials:Vault:SecretKey` | `…__Vault__SecretKey` | secret field holding the client secret/HMAC (fallbacks: `clientSecret`,`client_secret`,`hmac`,`secret`). |
| `ClientCredentials:Vault:ClientIdKey` | `…__Vault__ClientIdKey` | optional secret field for the outbound `client_id`. |
| `ClientCredentials:Vault:CacheTtlSeconds` | `…__Vault__CacheTtlSeconds` | credential cache TTL (default 300). |

> When `Source=Vault`, `AuthSettings:ClientSecret` is no longer used (only `TokenEndpoint`/`GrantType`).
> The Vault token must be provisioned to the workload (e.g. Kubernetes auth / injected env), not committed.

## Tenant vs client identity

`SeSClient:ClientId` was renamed to **`SeSClient:TenantId`** (the legacy `ClientId` key still binds as a
fallback). This value is the **tenant/deployment** identity used for Admin API, heartbeat, metrics and
sync enable/disable — it does **not** select outbound FHIR credentials (those are per-record `clientId`).
</content>
