# Secrets & Configuration

As of 2026-05-30 the SeS Transmitter no longer ships secrets in `appsettings.json`. Secret-bearing
values were blanked/placeholdered and must be supplied at runtime via **environment variables** (which
override config — `Program.cs` already calls `AddEnvironmentVariables()`) or a secret store / orchestrator.

> 🚨 **Rotate the previously-committed secrets.** The DB password, `AuthSettings.ClientSecret`, and
> `ShipAdminAuth.ClientSecret` were committed in git history and must be treated as **compromised**.
> Rotate them and, if policy requires, scrub history. Blanking the current file does **not** remove
> them from past commits.

## Values now blank/placeholder in `appsettings.json` (must be provided at runtime)

| Config path | Env var (note the `__` separator) | Notes |
|---|---|---|
| `AppSettings:ShipServerSqlDb:ConnectionString` | `AppSettings__ShipServerSqlDb__ConnectionString` | MySQL/PG connection string for the SHIP server DB. |
| `AppSettings:EmrDb:ConnectionString` | `AppSettings__EmrDb__ConnectionString` | Connection string for the extractor staging DB. |
| `ShipAdminAuth:ClientSecret` | `ShipAdminAuth__ClientSecret` | Tenant-level Admin API secret (stays tenant-scoped). |
| `AppSettings:Clients:{n}:ClientSecret` | `AppSettings__Clients__{n}__ClientSecret` | **Per-client outbound OAuth secret.** ISW-injected (see below). |
| `AppSettings:Clients:{n}:HmacSecret` | `AppSettings__Clients__{n}__HmacSecret` | **Per-client HMAC key.** ISW-injected (see below). |
| `EmrCallback:Proxy:Auth:Username` / `Password` | `EmrCallback__Proxy__Auth__Username` / `__Password` | Already placeholder (`__from_secret_store__`); supply only when proxy auth is `Basic`. |

> `AuthSettings:ClientSecret` is **no longer used** — outbound credentials come per-client from
> `AppSettings:Clients`. It may be left blank or removed. Only `AuthSettings:TokenEndpoint`/`GrantType`/`Scope`
> are read (non-secret).

## Per-client credentials via configuration (ISW-injected env vars, no Vault API access)

Outbound per-client credentials are resolved **from configuration** — the `AppSettings:Clients` list —
**not** by calling Vault. The application:

- **does not** know a Vault address or token (`VAULT_ADDR`/`VAULT_TOKEN` are gone);
- **does not** perform any Vault `list`/`read` API call;
- **does not** handle an `X-Vault-Token` header.

Instead, the **secret values are injected into the service runtime as environment variables before
startup** by the organization's standard **ISW secret-injection mechanism** — a HashiCorp Vault agent /
sidecar that reads the approved Vault path and writes the values into the process environment. .NET's
environment-variable configuration provider then binds those values over the committed placeholders. The
application only ever reads its own configuration.

Config shape (`appsettings.json`), with secrets as placeholders to be overridden at runtime:

```jsonc
"AppSettings": {
  "Hmac": {
    "Enabled": true,
    "RequireJwtAlso": true,
    "SignatureHeader": "X-SHIP-Signature",
    "TimestampHeader": "X-SHIP-Date",
    "NonceHeader": "X-SHIP-Nonce",
    "AllowedClockSkewSeconds": 300,
    "HmacAlgo": "HMACSHA256",
    "BypassPaths": [ "/health", "/swagger", "/docs", "/scalar" ]
  },
  "Clients": [
    {
      "ClientId": "ses-client-a",
      "ClientSecret": "<client-secret-to-be-replaced-by-value-in-vault>",
      "HmacSecret": "<hmac-secret-to-be-replaced-by-value-in-vault>",
      "Status": "ACTIVE"
    }
    // ses-client-b, ses-client-c, …
  ]
}
```

The env vars the ISW mechanism injects (one pair per client, index-aligned to the list order):

```
AppSettings__Clients__0__ClientSecret = <injected>
AppSettings__Clients__0__HmacSecret   = <injected>
AppSettings__Clients__1__ClientSecret = <injected>
AppSettings__Clients__1__HmacSecret   = <injected>
…
```

At startup the provider (`ConfigClientCredentialProvider`):

1. Reads `AppSettings:Clients` from configuration (placeholders now overridden by the injected env vars).
2. Keeps only clients with `Status = ACTIVE` **and** a non-placeholder secret; the `ClientId` is the
   outbound `client_id`. Non-secret material (token endpoint, grant type) comes from `AuthSettings`.
3. Loads them into memory and **reports the loaded set and skip count in the startup log**.

There are **no per-request lookups and no TTL cache** — the worker processes only the clients loaded at
startup; records for clients not loaded are skipped (left `Pending`). **Adding, removing or rotating a
client requires a restart.**

## Example (local development)

PowerShell — set the DB/admin secrets and the per-client secrets as environment variables (in production
these are ISW-injected, not set by hand):

```powershell
$env:AppSettings__ShipServerSqlDb__ConnectionString = "Host=...;Port=3306;Database=...;Username=...;Password=..."
$env:AppSettings__EmrDb__ConnectionString           = "Host=...;..."
$env:ShipAdminAuth__ClientSecret                    = "<dev-admin-secret>"
$env:AppSettings__Clients__0__ClientSecret          = "<ses-client-a-oauth-secret>"
$env:AppSettings__Clients__0__HmacSecret            = "<ses-client-a-hmac-key>"
dotnet run --project Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker
```

In containers, inject these as environment variables from your orchestrator's secret mechanism (the ISW
Vault agent in production; Kubernetes `Secret` → env or Docker `--env-file` for local). Do not bake them
into images.

## What remains in config (non-secret — OK to keep)

- URLs/endpoints: `AuthSettings:TokenEndpoint`, `ShipAdminApi:BaseUrl`, `ShipAdminAuth:TokenUrl`,
  `FhirRouting:*:BaseUrl`, `SourceDbSettings:ConnectionString` (Mongo, currently no credentials),
  callback templates.
- Identifiers/scopes/tuning: `SeSClient:TenantId`, `AuthSettings:Scope` (outbound authorization scope —
  the same for every SHIP target system, no longer per FHIR route), timeouts, batch sizes, heartbeat
  intervals, resource lists.
- Non-secret client metadata: `AppSettings:Clients:{n}:ClientId` / `Status` and the whole
  `AppSettings:Hmac` block (header names, algorithm, clock skew, bypass paths).

## Tenant vs client identity

`SeSClient:ClientId` was renamed to **`SeSClient:TenantId`** (the legacy `ClientId` key still binds as a
fallback). This value is the **tenant/deployment** identity used for Admin API, heartbeat, metrics and
sync enable/disable — it does **not** select outbound FHIR credentials (those are per-record `clientId`).
