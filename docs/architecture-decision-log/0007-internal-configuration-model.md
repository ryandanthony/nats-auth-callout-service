# 0007 — Internal configuration model

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

Every config source (JSON/YAML files, Kubernetes CRD, later a database) maps into one canonical in-memory model that the core consumes (ADR 0005). That model must express: named permission profiles per application (ADR 0001), OIDC-based service and user auth (ADR 0002), the issuer, validate, app `Match`, profile `Match` resolution pipeline (ADR 0003), the cross-application anonymous union (ADR 0004), secret references (ADR 0005), and per-application auth outcome event subjects (ADR 0008), templated subject variables bound to transformed claims (ADR 0009), per-profile session-store access (ADR 0010), and the assertion signing key (ADR 0011). This ADR fixes that canonical shape so all sources and the core agree on one structure.

## Decision

Two top-level models matching the two config layers (ADR 0005).

### Bootstrap model (startup, not hot-reloaded)

- `NatsUrl: string`
- `IssuerSeed: SecretRef` — signs minted user JWTs and the auth response.
- `ServiceUserSeed: SecretRef?` — the callout's own NATS user.
- `AssertionKey: SecretRef?` — dedicated keypair for signing identity assertions (ADR 0011); its public key is published to the keys KV bucket. Optional; only needed when assertions are enabled.
- `Source` — which routing/apps provider to use and its settings (path for a file directory, cluster-wide instance-matching label/selector for the CRD watch, connection string for a database).
- `Anonymous: AnonymousGlobal` — `TargetAccount: string`, `ExpirySeconds: int` (ADR 0004), plus `Events: EventSubjects?` for anonymous outcome events, since anonymous is not tied to one application (ADR 0008).

A `SecretRef` resolves either an inline literal or an external reference (Kubernetes Secret, mounted file, vault). Modeled as a discriminated shape: `{ kind: "inline" | "file" | "k8sSecret" | "vault", value/name/key... }`.

### Routing model (hot-reloaded, the aggregate)

`RoutingConfig`:

- `Applications: Application[]` — aggregated from the per-app units.

`Application`:

- `Name: string` — unique across the aggregate.
- `TargetAccount: string` — NATS account minted users are placed in.
- `Issuer: IssuerConfig`
  - `Authority: string` — OIDC issuer; selects candidate apps and JWKS.
  - `JwksUri: string?` — explicit override; when absent, resolved from the
    provider's OIDC discovery document
    (`{Authority}/.well-known/openid-configuration` -> `jwks_uri`). No
    provider-specific URL shape is assumed.
  - `JwksCacheSeconds: int`
  - `ValidateAudience: bool`, `Audience: string?` — validation, independent of routing (ADR 0003).
- `Match: ClaimMatch?` — app-level selector among same-issuer candidates (ADR 0003). Null when the issuer already identifies a single app.
- `AnonymousProfile: Grant?` — this app's contribution to the anonymous union (ADR 0004).
- `Profiles: Profile[]` — ordered; first match wins (ADR 0001).
- `IdentityPropagation: IdentityPropagationConfig?` — per-app identity propagation toggle (ADR 0012). `{ Enabled: bool }`, default enabled. When disabled, the callout writes no token/assertion records and adds no keys-bucket read for this app, and `AutoAddSessionAccess` on its profiles is invalid.
- `Events: EventSubjects?` — auth outcome event subjects for this application (ADR 0008). Null disables event publishing for the application.

`EventSubjects` (shared by applications and the global anonymous config):

- `Success: string?` — subject for successful auth outcomes.
- `Failure: string?` — subject for failed/denied auth outcomes.
- Explicit subjects only; there is no templated `{outcome}` form. Either may be null to publish only one side; both null (or the whole block omitted) disables events (ADR 0008).

`Profile`:

- `Name: string`
- `Match: ClaimMatch` — required; selects this profile post-validation.
- `Grant` (see below) — static publish/subscribe subjects.
- `NameClaim: string` — claim used as the minted JWT name.
- `Variables: TemplateVariable[]` — named variables bound to (optionally transformed) claims (ADR 0009).
- `PublishTemplates: string[]`, `SubscribeTemplates: string[]` — subject patterns with `{name}` placeholders resolved from `Variables` (ADR 0009).
- `AutoAddSessionAccess: bool` — when true, the minted grant also gets read access to this application's token KV bucket `callout.apps.{app-slug}.oidc-tokens.>` (ADR 0010). Default false.
- `FallbackExpirySeconds: int` — used when the token carries no `exp`.

`TemplateVariable` (ADR 0009):

- `Name: string` — the `{name}` placeholder.
- `Claim: string` — claim path it reads (nested paths supported).
- `Transforms: Transform[]` — ordered transform pipeline (lowercase, replace, sanitize, hash, prefix/suffix, require-safe, ...).
- `Required: bool` — deny when the variable resolves to no value (generalizes the old `RequireScope`).

`Grant` (shared static grant shape):

- `Publish: string[]`
- `Subscribe: string[]`

`ClaimMatch` (shared matcher for app and profile selection, ADR 0003):

- A map of `claimPath -> string[]`. A key matches when the claim at that path (nested paths and arrays supported) contains any listed value (OR within a key). All keys must match (AND across keys). An empty/absent match is only valid for an application whose issuer already uniquely identifies it.

### Derived, not stored

- The effective anonymous grant is computed as the de-duplicated union of every application's `AnonymousProfile`, minted into `Anonymous.TargetAccount` (ADR 0004). It is not itself a configured application.
- Candidate-app-by-issuer and JWKS handles are indexes built from `Applications`, not persisted config.
- Resolved `EventSubjects` drive best-effort, non-blocking publishing on each auth outcome (ADR 0008); the subjects are config, the emitted events are not.

## Consequences

- One canonical model backs all sources; each source only implements deserialize/map into these types.
- `Grant` and `ClaimMatch` are reused across profiles, app selection, and the anonymous union, keeping matching and grant semantics in one place.
- Validation runs against this model: unique application names, no two applications indistinguishable by issuer plus `Match`, every profile has a `Match`, every template placeholder resolves to a declared variable with a valid claim path and known transforms, resolvable secret references, valid subject templates. Failures follow ADR 0006 (idle or keep-last-good).
- The database source (future) maps rows into the same `Application` aggregate, so no core changes are needed to add it.

## Related

- Design: [04 — Configuration](../design/04-configuration.md#canonical-model-adr-0007), [02 — Authorization model](../design/02-authorization-model.md#concepts).
- ADRs: [0001](./0001-named-permission-profiles.md), [0002](./0002-service-auth-via-oidc-not-known-keys.md), [0003](./0003-resolution-order-issuer-validate-app-profile.md), [0004](./0004-anonymous-grant-is-union-across-apps.md), [0005](./0005-config-abstraction-and-sources.md), [0008](./0008-publish-auth-outcome-events.md), [0009](./0009-templated-subject-variables.md), [0010](./0010-session-jwt-store-in-kv.md), [0011](./0011-signed-identity-assertion.md) — this model expresses them all.
