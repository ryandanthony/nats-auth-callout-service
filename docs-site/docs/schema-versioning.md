---
sidebar_position: 5
---

# Schema versioning

Four externally-visible surfaces, two versioning idioms
([ADR 0015](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0015-schema-versioning-strategy.md),
[doc 09](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/design/09-schemas-and-versioning.md)):

| Surface | Direction | Versioning |
|---------|-----------|------------|
| [Auth outcome event](./operations/event-schema) | produced | Integer `schemaVersion` |
| Discovery descriptor | produced | Integer `schemaVersion` |
| [Application config file](./configuration/application-reference) | consumed | Integer `schemaVersion` |
| Bootstrap config file | consumed | Integer `schemaVersion` |
| Kubernetes CRD (`AuthApplication`) | consumed | Kubernetes `apiVersion` |

## Payload surfaces (events, discovery, files)

Additive changes (new optional fields) **keep** the current version.
Breaking changes bump the integer. Consumers should ignore unknown fields
and gate their own logic on the version they see, never assume future
fields are absent.

For **produced** surfaces (events, discovery), a missing `schemaVersion` on
a *consumer's* side would be a bug in this service — it's always present.
For **consumed** surfaces (config files), a missing `schemaVersion` is
treated as `1` during a migration window, then rejected once that window
closes (fail closed on ambiguity, per [ADR 0006](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0006-config-reload-and-failure-semantics.md)).
A `schemaVersion` newer than supported rejects the whole batch.

## Kubernetes CRD

The CRD uses Kubernetes' own `apiVersion` mechanism instead of an integer:
`v1` is served and stored today. A future breaking change adds a new
served version (`v2`) plus a conversion (webhook, or a none-strategy with
defaulting for simple renames); Kubernetes converts on read/write, and the
service's provider always deserializes the **storage** version into the
canonical model — it never branches on served version at the call site.
Deprecated fields survive at least one served version, per Kubernetes
convention.

## Schema artifacts in the repo

```
schemas/
  events/auth-outcome/v1.json          # JSON Schema, consumer contract tests
  discovery/descriptor/v1.json         # JSON Schema
  config/application/v1.json           # JSON Schema for files (strict)
  config/bootstrap/v1.json             # JSON Schema for bootstrap file
deploy/crd/
  authapplication-crd.yaml             # CustomResourceDefinition (OpenAPI v3 validation)
```

Contract/golden tests assert the emitted event and discovery descriptor
validate against their JSON Schema, and that the file schema and the CRD
`spec` map to the identical canonical model for equivalent input
([ADR 0007](https://github.com/ryandanthony/nats-auth-callout-service/blob/main/docs/architecture-decision-log/0007-internal-configuration-model.md)),
so the two config sources cannot silently diverge.
