---
sidebar_position: 4
---

# Troubleshooting

## Deny reasons → likely causes

Every denial is logged with its reason code and, for successful auth
outcomes with `events` configured, published on the outcome event stream
(see [Event schema](./operations/event-schema)).

| Reason (event `reason` field) | Likely cause | Where to look |
|---|---|---|
| `invalid_token` | Expired token, bad signature, unknown `kid`, or a disallowed algorithm (`HS*`/`none` never accepted). | Check the token's `exp`, and that the issuer's `allowedAlgorithms` includes the token's `alg`. Clock skew beyond `clockSkew` (default `60s`, capped `300s`) also lands here. |
| `no_issuer_match` | Token's `iss` doesn't match any configured application's `issuer.authority`. | Confirm the application file's `issuer.authority` matches the token's `iss` exactly, and that the app was actually loaded (`config-active` health check, `/readyz`). |
| `no_app_match` | Issuer matched but no application's `match` predicate was satisfied. | Compare the token's claims against the application's `match` map — remember AND across keys, OR within a key's values. |
| `ambiguous_app` | More than one application matched the same issuer + claims. | Tighten each application's `match` so exactly one matches per token shape — ambiguity always denies, never picks one. |
| `no_profile_match` | Application resolved, but no profile's `match` was satisfied. | Check the matched application's `profiles[].match` against the token's claims; profiles are first-match-wins, order matters. |
| `grant_construction_failed` | A `required` template variable resolved to nothing, or a `require-safe` transform rejected an unsafe value. | Check the claim a `required: true` variable reads is actually present, and that `sanitize`/`require-safe` transforms aren't rejecting a legitimately unusual claim value. |

## Config rejected at load / reload

The [config validator's](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/src/AuthCallout.Contracts/Validation/RoutingConfigValidator.cs)
error catalogue (a whole batch is rejected atomically on any of these,
last-good stays active — [ADR 0006](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0006-config-reload-and-failure-semantics.md)):

| Error code | Cause | Fix |
|---|---|---|
| `duplicate-application-name` | Two applications share a `name`. | Rename one — names must be unique across the aggregate. |
| `duplicate-application-slug` | Two application names slugify to the same value ([ADR 0013](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0013-kv-bucket-naming-contract.md)). | Rename one so their slugs no longer collide. |
| `invalid-application-slug` | An application's `name` can't be slugified into a valid bucket-name component ([ADR 0013](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0013-kv-bucket-naming-contract.md)). | Use a `name` that slugifies cleanly (alphanumeric, hyphens). |
| `ambiguous-null-match` | An application has no `match` but shares its issuer `authority` with other applications. | Add a `match` — an absent match is only valid when the issuer alone identifies a single application. |
| `indistinguishable-applications` | Two applications share an issuer and an identical `match` predicate. | Differentiate the matches so exactly one application can win per token. |
| `divergent-issuer-config` | Two applications share an issuer `authority` but their `issuer` blocks differ (audience, clockSkew, allowedAlgorithms, jwksUri, cache/refresh). | Make every application on one authority use an identical `issuer` block — validation runs once per token against the shared authority's parameters ([ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md)). |
| `application-name-required` / `target-account-required` | Missing `name` / `targetAccount`. | Add the field. |
| `issuer-authority-required` | Empty `issuer.authority`. | Set the OIDC authority URL. |
| `clock-skew-negative` | Negative `clockSkew`. | Use a non-negative duration (default `60s`, max `300s`). |
| `clock-skew-exceeds-cap` | `clockSkew` above the `300s` hard cap ([ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md)). | Use `300s` or less. |
| `algorithm-not-allowed` | `allowedAlgorithms` lists an algorithm outside the asymmetric set (e.g. `HS256`, `none`). | Only `RS*`/`ES*`/`PS*` are permitted — HMAC enables algorithm confusion against JWKS keys. |
| `algorithms-required` | Empty `allowedAlgorithms`. | List at least one asymmetric algorithm (`RS256`, `ES256`, ...). |
| `audience-required-when-validating` | `validateAudience: true` with no `audience`. | Add `audience`, or explicitly set `validateAudience: false` if your provider omits `aud`. |
| `profile-name-required` | A profile has an empty `name`. | Name every profile. |
| `auto-session-access-requires-propagation` | A profile sets `autoAddSessionAccess: true` while the application's `identityPropagation.enabled` is `false`. | Enable identity propagation for the app, or remove `autoAddSessionAccess` — nothing exists to read when disabled. |
| `duplicate-variable-name` | Two `variables` entries share a `name` within one profile. | Rename one. |
| `variable-name-required` | A template variable has an empty `name`. | Name every variable. |
| `variable-claim-required` | A template variable has an empty `claim`. | Set the claim path. |
| `malformed-replace-transform` | A `replace` transform is missing a non-empty `from` or a `to`. | Provide both `from` and `to`. |
| `malformed-affix-transform` | A `prefix`/`suffix` transform is missing its literal string argument. | Provide the missing argument. |
| `unknown-template-variable` | A `publishTemplates`/`subscribeTemplates` entry references a `{name}` placeholder with no matching declared `variables` entry. | Declare the variable, or fix the placeholder name. |

Bootstrap-file specific (never hot-reloaded, so these only surface at
startup):

| Error code | Cause | Fix |
|---|---|---|
| `nats-url-required` | Empty `natsUrl`. | Set the NATS server URL. |
| `reserved-kid` | An `assertionKeys` entry uses the reserved `kid` value `current` ([ADR 0013](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0013-kv-bucket-naming-contract.md)). | Use a different `kid`; `current` is reserved as the active-key pointer. |
| `duplicate-kid` | Two `assertionKeys` entries share a `kid`. | Rename one — `kid`s must be unique. |
| `assertion-keyset-active-count` | Zero or more than one `assertionKeys` entry marked `active`. | Exactly one entry must have `active: true` ([ADR 0014](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0014-key-rotation-orchestration.md)). |
| `unknown-source-kind` | `source.kind` isn't `files`, `k8s`, or `postgres`. | Fix the typo. |
| `files-directory-required` / `k8s-selector-required` / `postgres-settings-required` | The active source is missing its kind-specific required field. | Add `source.directory` (files), `source.labelSelector` (k8s), or the postgres connection settings. |
| `postgres-notify-channel-required` / `postgres-poll-interval-positive` | PostgreSQL source is missing its `notifyChannel`, or `pollInterval` is non-positive. | Set `source.notifyChannel`; use a positive `pollInterval`. |
| `warn-staleness-positive` / `max-staleness-exceeds-warn` / `reconnect-backoff-positive` | Non-positive or inverted staleness thresholds. | `warnStaleness` and `reconnectBackoff` must be positive; `maxStaleness` must exceed `warnStaleness` ([ADR 0017](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0017-watch-loss-and-connection-loss-semantics.md)). |
| `jwks-refresh-interval-positive` / `jwks-fetch-timeout-positive` | Non-positive JWKS timing. | Use positive durations. |
| `max-degree-of-parallelism-positive` / `per-request-timeout-positive` / `queue-capacity-positive` | Non-positive concurrency settings. | Use positive values. |
| `anonymous-target-account-required` / `anonymous-expiry-positive` | Missing/invalid global anonymous settings. | Set `anonymous.targetAccount` and a positive `anonymous.expiry`. |
| `malformed-secret-ref` | A `SecretRef`'s `kind` is missing the locator fields it requires (e.g. `file` without `path`). | Match the `kind` to its required fields — see the [SecretRef reference](./configuration/bootstrap-reference#secretref). |

## Common misconfigurations

**`/readyz` stuck at 503 with `"state":"Starting"` for a long time.**
Usually JWKS prewarm can't reach an issuer's discovery endpoint (network,
DNS, or a self-signed cert the callout doesn't trust), or the config
source has never produced a first valid batch. Check the `jwks` and
`config-source` health check entries in the `/readyz` JSON body for the
specific issuer/source at fault.

**Client connects but every KV read fails with "stream not found" (self-hosted NATS).**
Almost always a missing cross-account JetStream export/import: the
identity-store KV buckets live in the callout's own account, and every
target account needs `$JS.API.>` (service) and `$KV.>` (stream) imported
from it. See [NATS server configuration](./getting-started/nats-server-config#why-every-target-account-imports-jsapi-and-kv-from-auth).
A target account that *also* sets `jetstream: enabled` locally will shadow
the import the same way — remove it if the account has no JetStream assets
of its own.

**A profile's `autoAddSessionAccess` doesn't work.**
Confirm the application's `identityPropagation.enabled` is `true` — the
config validator rejects `autoAddSessionAccess: true` on a disabled
application (`auto-session-access-requires-propagation` above), so this
usually fails at config load, not at runtime.

**Anonymous clients get no subjects at all.**
No application contributed an `anonymousProfile` — this is a valid,
deliberate opt-out per application, not a bug. Add an `anonymousProfile`
with `publish`/`subscribe` subject lists to any application that should
contribute to the union.
