---
sidebar_position: 2
---

# Event schema and reason codes

Schema: [`schemas/events/auth-outcome/v1.json`](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/schemas/events/auth-outcome/v1.json)
([doc 09](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/09-schemas-and-versioning.md#1-auth-outcome-event),
[ADR 0008](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0008-publish-auth-outcome-events.md)).
Published best-effort per decision, secret-free, to the per-application
`events.success` / `events.failure` subject (or the global anonymous
subject).

## Payload (`schemaVersion: 1`)

```json
{
  "schemaVersion": 1,
  "timestamp": "2026-09-06T22:00:00Z",
  "outcome": "success",
  "reason": "granted",
  "application": "notes",
  "profile": "editor",
  "principal": "user@example.com",
  "issuer": "https://id.example.com/realms/main",
  "userNKey": "UED..."
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `schemaVersion` | integer | yes | `1`. Consumers reject unknown majors. |
| `timestamp` | RFC 3339 string | yes | Decision time, UTC. |
| `outcome` | enum | yes | `success` \| `failure`. |
| `reason` | enum string | yes | See [reason codes](#reason-codes) below. New codes are additive (no version bump); unknown codes should be treated as a generic failure. |
| `application` | string | no | Absent when no application was selected. |
| `profile` | string | no | Present on success. |
| `principal` | string | no | `sub`/`azp`-style identifier, never the raw token. |
| `issuer` | string | no | Original OIDC issuer. |
| `userNKey` | string | no | The connection's minted user NKey. |

`additionalProperties: true` on the JSON Schema so v1 consumers survive
additive changes.

## Reason codes

| `reason` | `outcome` | Meaning | Likely cause |
|----------|-----------|---------|---------------|
| `anonymous` | `success` | No token presented; anonymous union grant applied. | Client connected with no bearer credential — expected for anonymous clients. |
| `invalid_token` | `failure` | Token present but failed validation (expired, bad signature, unknown `kid`, disallowed algorithm). | See [Troubleshooting](../troubleshooting#deny-reasons--likely-causes). |
| `no_issuer_match` | `failure` | Token's `iss` doesn't match any configured application's issuer. | Wrong environment/realm, or the application isn't configured yet. |
| `no_app_match` | `failure` | Issuer matched, but no application's `match` predicate was satisfied (or more than one issuer-level candidate exists with no distinguishing `match`). | Missing/incorrect `azp` or other app-selector claim. |
| `ambiguous_app` | `failure` | More than one application matched — ambiguity denies, never guesses. | Two applications configured with overlapping issuer + `match`. |
| `no_profile_match` | `failure` | Application resolved, but no profile's `match` was satisfied. | Missing role/claim the profiles expect; check the app's `profiles[].match`. |
| `grant_construction_failed` | `failure` | A `required` template variable had no value, or a transform produced an unsafe result (`require-safe`). | A claim the profile's `variables` depends on is missing or empty. |
| `granted` | `success` | Token validated, application + profile matched, grant minted. | Normal successful auth. |

An anonymous grant emits `outcome: success, reason: anonymous` — anonymous
is a success case, not a fallback from failure.

## Consuming the stream

Treat this as a **best-effort** feed, not a guaranteed-complete audit log
— a publish failure is logged and metered but never blocks or fails the
underlying auth decision ([ADR 0008](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0008-publish-auth-outcome-events.md)).
If you need guaranteed-complete audit, treat this as a separate concern
layered on top (e.g. a JetStream consumer with its own durability).
