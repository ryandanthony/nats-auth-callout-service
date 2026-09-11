---
sidebar_position: 3
---

# Application file reference

Schema: [`schemas/config/application/v1.json`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/schemas/config/application/v1.json).
One file per application ([files source](./sources/overview#files)); the
identical shape backs the Kubernetes CRD's `spec` and PostgreSQL's `spec`
JSONB column ([ADR 0007](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0007-internal-configuration-model.md)).
Strict: unknown top-level and nested-object fields are rejected (`match`/
claim-match maps are the deliberate exception — keys are arbitrary claim
paths).

## Full example

```yaml
schemaVersion: 1
name: notes
targetAccount: NOTES
issuer:
  authority: https://id.example.com/realms/main
  jwksUri: null
  jwksCache: 5m
  refreshInterval: null
  validateAudience: true
  audience: [nats]
  clockSkew: 60s
  allowedAlgorithms: [RS256, ES256]
match:
  azp: [notes-web]
identityPropagation:
  enabled: true
anonymousProfile:
  publish: []
  subscribe: [notes.public.>]
events:
  success: notes.events.auth.success
  failure: notes.events.auth.failure
profiles:
  - name: editor
    match:
      realm_access.roles: [notes-editor]
    grant:
      publish: [notes.{tenant}.>]
      subscribe: [notes.{tenant}.>]
    nameClaim: sub
    autoAddSessionAccess: false
    variables:
      - name: tenant
        claim: tenant_id
        required: true
        transforms: [lowercase, sanitize]
    publishTemplates: ["notes.{tenant}.outbox.>"]
    subscribeTemplates: ["notes.{tenant}.inbox.>"]
```

## Top-level fields

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `schemaVersion` | integer | yes | Must be `1`. Missing treated as `1` during a migration window, then rejected. |
| `name` | string | yes | Application name, unique across the aggregate. |
| `targetAccount` | string | yes | NATS account minted users for this application are placed in. |
| `issuer` | [Issuer](#issuer) | yes | The OIDC authority + strict validation parameters ([ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md)). |
| `match` | [ClaimMatch](#claimmatch) | no | App selector among same-issuer candidate applications. |
| `identityPropagation` | object `{ enabled }` | no | Per-application toggle: `enabled` (boolean), default `true` ([ADR 0012](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0012-identity-propagation-toggle-and-discovery.md)). |
| `anonymousProfile` | [Grant](#grant) | no | This application's contribution to the anonymous union grant ([ADR 0004](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0004-anonymous-grant-is-union-across-apps.md)). Omitting it is a valid opt-out. |
| `events` | [EventSubjects](#eventsubjects) | no | Auth-outcome event subjects for this application ([ADR 0008](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0008-publish-auth-outcome-events.md)). |
| `profiles` | array of [Profile](#profile) | yes | Ordered; first-match-wins. |

## `Issuer`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `authority` | string | yes | The OIDC authority. Discovery is fetched from `{authority}/.well-known/openid-configuration`. |
| `jwksUri` | string \| null | no | Optional override; otherwise resolved from OIDC discovery. |
| `jwksCache` | duration string | yes | Cache lifetime; should be ≥ `refreshInterval`. |
| `refreshInterval` | duration string \| null | no | Overrides the bootstrap `jwks.refreshInterval` for this issuer. |
| `validateAudience` | boolean | no (default `true`) | When `true`, `audience` becomes required. |
| `audience` | array of strings | required if `validateAudience: true` | Accepted audiences. |
| `clockSkew` | duration string | no (default `60s`) | Capped at `300s` ([ADR 0016](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0016-token-validation-parameters.md)). |
| `allowedAlgorithms` | array of enum | no | Asymmetric only: `RS256/384/512`, `ES256/384/512`, `PS256/384/512`. `HS*`/`none` are always rejected. |

## `ClaimMatch`

A map of claim path → accepted values, e.g. `{ realm_access.roles: [notes-editor] }`.
Nested paths supported. Array claims match if they contain **any** listed
value (OR within a key); multiple keys must **all** match (AND across keys).
See [Authorization model → Claim match](../guides/authorization-model#claim-match).

## `Grant`

| Field | Type | Notes |
|-------|------|-------|
| `publish` | array of strings | Publish subject patterns. |
| `subscribe` | array of strings | Subscribe subject patterns. |

## `EventSubjects`

| Field | Type | Notes |
|-------|------|-------|
| `success` | string | Subject for successful outcomes. |
| `failure` | string | Subject for failed outcomes. |

## `Profile`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | string | yes | Profile name (unique within the application). |
| `match` | [ClaimMatch](#claimmatch) | yes | Every profile must declare a match. |
| `grant` | [Grant](#grant) | yes | Static publish/subscribe subjects. |
| `nameClaim` | string | yes | Which claim names the principal in outcome events; falls back to `sub` if the configured claim is missing ([D19](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/QUESTIONS.md)). |
| `autoAddSessionAccess` | boolean | no | Grants read of `callout.apps.{app-slug}.oidc-tokens.>` for this profile's connections. Invalid if the application's `identityPropagation.enabled` is `false`. |
| `variables` | array of [TemplateVariable](#templatevariable) | no | Named template variables bound to claims. |
| `publishTemplates` | array of strings | no | Publish subject patterns with `{name}` placeholders. |
| `subscribeTemplates` | array of strings | no | Subscribe subject patterns with `{name}` placeholders. |

## `TemplateVariable`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `name` | string | yes | The `{name}` placeholder used in templates. |
| `claim` | string | yes | Claim path to read (nested supported). |
| `required` | boolean | no | A `required` variable with no resolved value denies the connection. |
| `transforms` | array of [Transform](#transform) | no | Ordered transform pipeline. |

## `Transform`

Either a bare name — `lowercase`, `uppercase`, `sanitize`, `hash`,
`require-safe` — or a single-key object:

| Form | Fields | Notes |
|------|--------|-------|
| `replace` — `{ replace: { from, to } }` | `from` (required, non-empty), `to` | Literal substring replace. |
| `{ prefix: "..." }` | `prefix` | Prepend a literal string. |
| `{ suffix: "..." }` | `suffix` | Append a literal string. |

See [Authorization model → Templated subject variables](../guides/authorization-model#templated-subject-variables)
for the full worked example and why transforms exist (NATS subject
characters `.`, `*`, `>` are special).
