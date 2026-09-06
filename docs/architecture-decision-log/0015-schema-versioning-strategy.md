# 0015 — Schema versioning strategy

- **Status:** proposed
- **Date:** 2026-09-06
- **Deciders:** Ryan

## Context

The service has several externally-visible data shapes, each with a different audience, transport, and rate of change:

- **Auth outcome events** ([ADR 0008](./0008-publish-auth-outcome-events.md)) — published to NATS, consumed by other services; already carry a `schemaVersion`.
- **Discovery descriptor** ([ADR 0012](./0012-identity-propagation-toggle-and-discovery.md)) — published to KV, consumed by clients/subscribers; already carries a `schemaVersion`.
- **Routing config as files** (JSON/YAML, [ADR 0005](./0005-config-abstraction-and-sources.md)) — authored by operators, read by the service.
- **Routing config as a Kubernetes CRD** ([ADR 0005](./0005-config-abstraction-and-sources.md)) — authored by operators, read by the service via the k8s API, which has its own `apiVersion` machinery.

These use two different, incompatible versioning idioms, and that is correct: a JSON event on a subject and a Kubernetes resource evolve under different rules. Without a single policy, each surface would drift into ad-hoc handling. This ADR fixes **one** versioning strategy per surface and the compatibility rules between them. The concrete field-level schemas live in [09 — Schemas and versioning](../design/09-schemas-and-versioning.md).

## Decision

### Two versioning idioms, by transport

| Surface | Idiom | Where the version lives |
|---------|-------|-------------------------|
| Auth outcome events | Integer `schemaVersion` in the payload | JSON body |
| Discovery descriptor | Integer `schemaVersion` in the payload | JSON body |
| Config files (JSON/YAML) | Integer `schemaVersion` **field** in each file | file body |
| Kubernetes CRD | Kubernetes `apiVersion` (`vNalphaM`/`vNbetaM`/`vN`) | resource `apiVersion` + CRD `versions[]` |

**Payload-carried surfaces** (events, discovery, files) use a single monotonically increasing **integer** `schemaVersion`. No semver, no dotted versions: these are internal data contracts, not published libraries, and an integer keeps consumer branching trivial.

**The CRD** uses Kubernetes-native `apiVersion` because the k8s API server, `kubectl`, and stored objects all key off it. Reusing an integer here would fight the platform.

### Compatibility rules (payload surfaces)

- **Additive = same version.** Adding an optional field that older consumers can ignore does **not** bump `schemaVersion`. Producers may emit new optional fields at the current version; consumers must ignore unknown fields.
- **Breaking = bump.** Removing/renaming a field, changing a type, or changing the meaning of a value bumps `schemaVersion` by one.
- **Producer emits one version at a time.** The service emits the current `schemaVersion` for each surface; it does not multiplex versions. A breaking change is a coordinated rollout.
- **Consumers gate on version.** Consumers read `schemaVersion` first and reject/handle unknown majors explicitly (fail closed for anything security-relevant).

### Compatibility rules (CRD)

- The service's k8s provider watches **one storage version** but the CRD may serve multiple `versions[]`; the API server converts. v1 is served as soon as the shape is stable; pre-stable shapes use `v1alpha1`/`v1beta1`.
- Field deprecation follows Kubernetes convention: a field stays for at least one served version after deprecation.
- The canonical internal model ([ADR 0007](./0007-internal-configuration-model.md)) is the conversion target; the CRD version and the file `schemaVersion` both map **into** it, so the core never sees a versioned shape.

### Cross-surface independence

Each surface versions independently. Bumping the event `schemaVersion` does not touch the CRD `apiVersion` or the file `schemaVersion`. They are only related through the canonical model they map into (config surfaces) or the decision they describe (event/discovery).

### Validation gates (fail closed, [ADR 0006](./0006-config-reload-and-failure-semantics.md))

- A config file or CR whose declared version is **unknown/newer than the service supports** is rejected with a clear reason; the batch is not applied (keep last-good).
- A config surface missing `schemaVersion` (files) is treated as the lowest supported version only during a defined migration window, then required.

## Consequences

- One documented rule per surface; no per-field improvisation.
- Integer `schemaVersion` keeps event/discovery/file consumers simple and matches the existing ADR 0008/0012 discipline.
- The CRD stays idiomatic to Kubernetes, so `kubectl`, conversion webhooks, and stored-version upgrades work as operators expect.
- Additive changes are cheap (no version churn); only genuinely breaking changes force a coordinated bump.
- The canonical model absorbs version differences between the two config sources, so adding a file or CRD version never changes the decision core.
- Requires the config loaders to carry a version-aware mapping layer and the CRD to ship a conversion strategy when it reaches a second served version.

## Related

- Design: [09 — Schemas and versioning](../design/09-schemas-and-versioning.md) (the concrete schemas), [04 — Configuration](../design/04-configuration.md), [08 — Technical architecture (C#)](../design/08-technical-architecture-csharp.md).
- ADRs: [0005](./0005-config-abstraction-and-sources.md) (sources), [0006](./0006-config-reload-and-failure-semantics.md) (reject-on-invalid), [0007](./0007-internal-configuration-model.md) (canonical model), [0008](./0008-publish-auth-outcome-events.md) (event `schemaVersion`), [0012](./0012-identity-propagation-toggle-and-discovery.md) (discovery `schemaVersion`).
