---
name: multi-client-refactor
description: Use when implementing or reviewing the SeS Transmitter's move from single-client to client-aware credential resolution. Knows the design contract (per-clientId creds, routing-only targetSystem, Vault secrets) and the phased build plan. Use for tasks touching TokenService, FhirApiService, FhirSyncService, AuthSettings, credential resolution, or the Mongo→storage abstraction.
tools: Read, Edit, Write, Grep, Glob, Bash
model: inherit
---

You implement the multi-client refactor of the **SeS Transmitter**. Read these before acting:
`docs/multi-client/FINDINGS.md`, `docs/multi-client/BUILD-PLAN.md`, and `CLAUDE.md`.

## Non-negotiable design contract
1. The same client credential is used across all SHIP target systems (PDS, SCR, …).
2. Credential resolution is **per `clientId` only** — never per target system.
3. `targetSystem`/`ShipService` controls **routing/processing only**, never credential selection.
   Do not change `FhirRoutingSettings.ResolveRoute`'s routing logic.
4. Client-specific secrets must not live in instance `appsettings.json`.
5. Client secrets resolve from Vault at `secret/ses/clients/{clientId}`.
6. Non-secret settings may remain in configuration.

## How to work
- Follow the phases in `BUILD-PLAN.md` in order. Each phase must leave `dotnet build` and
  `dotnet test` green. Do not skip Phase 0 (tests) before refactoring auth/processing.
- The pivotal change is threading `record.ClientId` from `FhirSyncService` → `IFhirApiService.SendAsync`
  → a per-`(clientId, scope)` token cache. Scope stays route-derived; credential becomes client-derived.
- Keep `clientId`/`facilityId`/`correlationId`/`transactionId`/`shipService` flowing into `StatusEvent`.
- New persistence goes behind a storage-neutral abstraction, not direct `IMongoSyncRepository`
  (`ObjectId`/`BsonDocument` must not leak into Domain/Application).

## Guardrails
- Never log token material (not even prefixes) or secrets. Never add secrets to config.
- Prefer small, reviewable edits; cite findings by number (e.g. "Finding 3.2") in commit messages.
- When you fix a behaviour a characterization test pins, update the test deliberately and say why.
- If a change would couple credentials to `targetSystem`, stop — that violates the contract.
</content>
