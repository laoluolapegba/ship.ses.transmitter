# ⚠️ Security Note — EMR Callback URL is Trusted (SSRF / Open-Redirect)

**Status:** Accepted risk for now — **trust the stored URL**. Flagged for DevOps / security
team to verify before this is treated as production-hardened.
**Owner to action:** DevOps + Platform/Security team.
**Related finding:** [`FINDINGS.md`](./FINDINGS.md) §5.1.
**Date raised:** 2026-05-30.

## What happens today

`EmrCallbackWorker` delivers status callbacks by POSTing to a URL taken **directly** from the
stored record, with no validation or allowlisting:

- `Worker/EmrCallbackWorker.cs` → `ResolveTargetUrlAsync` returns `evt.EmrTargetUrl`, falling back
  to `patient.ClientEMRCallbackUrl` (from `MongoSyncRepository.GetPatientByTransactionIdAsync`).
- The returned URL is passed straight to `HttpClient.SendAsync` in `SendToEmrAsync`.

The callback URL originates upstream (ingestor / EMR-supplied during submission) and is persisted
on the sync record / status event. The transmitter does **not** check that the host belongs to the
owning client or to an approved destination.

## Why it's a risk

- **SSRF:** a record with a crafted URL (e.g. `http://169.254.169.254/...`, internal admin hosts,
  cloud metadata endpoints) would cause the worker to make outbound requests to attacker-chosen
  internal targets from inside the trust boundary.
- **Open-redirect / data exfiltration:** callback bodies (status, transactionId, correlationId)
  would be delivered to an unintended host.
- Not yet **client-scoped:** there is no check that the callback host matches the client that owns
  the record.

## Current decision

We **trust the stored callback URL** for now. The data path is internal and the URL is set during a
controlled submission flow, so the immediate exposure is considered low **provided** the following
hold — which DevOps/Security must confirm:

## ✅ What DevOps / Security must verify

- [ ] Callback URLs are only ever written by trusted components (ingestor / submission API) and
      cannot be injected by an external caller.
- [ ] Network egress from the transmitter is restricted (egress firewall / NACL) so it cannot reach
      cloud metadata endpoints (`169.254.169.254`), internal admin planes, or arbitrary internal hosts.
- [ ] The submission flow validates/normalizes the EMR callback URL **at the point of capture**
      (scheme = https, host on a per-client allowlist).
- [ ] Logs/alerts exist for callback deliveries to unexpected hosts.

## Hardening shipped (Build Plan Phase 5, 2026-05-30) — enforcement is OPT-IN

A host allow-list guard now exists but **defaults to trust** (no behaviour change) so the accepted-risk
decision above still holds until DevOps switches it on:

- `ICallbackUrlValidator` runs in `EmrCallbackWorker` before every POST. It **always** rejects missing,
  non-absolute, or non-http(s) URLs. Host allow-listing is gated by `EmrCallback:Validation:Enabled`
  (default `false` = trust).
- To **enforce**, set `EmrCallback:Validation:Enabled = true` and provide `AllowedHosts` (global) and/or
  `PerClient` (keyed by clientId). `*.example.org` matches subdomains; `RequireHttps` can force https.
  A disallowed URL is **dead-lettered** (not retried forever); callbacks also dead-letter after
  `EmrCallback:MaxAttempts`.
- `EmrTargetUrl` is now persisted on the `StatusEvent` when the PENDING event is seeded, so resolution
  no longer depends on a patient-only lookup (works for non-patient resources). The `x-client-id` header
  is sent to the EMR for traceability.

### ✅ Still owed by DevOps / Security

- Decide whether to flip `EmrCallback:Validation:Enabled = true` and populate the allow-list (per the
  verification checklist above). Until then, URLs remain trusted by design.
- The per-client allow-list is currently **config-driven**; sourcing it from the client registry/Admin API
  is a future enhancement.
- Per-client callback **auth** (signing / bearer) beyond `x-client-id` is not yet implemented.

See [`SECRETS-AND-CONFIG.md`](./SECRETS-AND-CONFIG.md) for the config keys.
</content>
