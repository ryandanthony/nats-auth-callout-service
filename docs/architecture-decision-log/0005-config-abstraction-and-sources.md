# 0005 — Config abstraction with pluggable sources

- **Status:** proposed
- **Date:** 2026-09-05
- **Deciders:** Ryan

## Context

Routing and application configuration must live in different backends over time (Kubernetes, YAML, and JSON to start, a database later) without coupling the core to any one storage mechanism, and applications must be added or removed as discrete units.

## Decision

Configuration is split into two layers behind a common provider interface.

### Two layers

1. **Bootstrap/connection config** — NATS `Url`, `IssuerSeed`, `ServiceUserSeed`, and which routing/apps source to use. Loaded once at startup from a simple fixed source (local file / flags). Not hot-reloaded.
2. **Routing/apps config** — applications, profiles, anonymous, and JWT/JWKS settings. Served by the pluggable, auto-refreshing provider interface.

### Sources

- **JSON** and **YAML**: a directory of per-application files (e.g. `notes.yaml`, `tasks.yaml`), each aggregated into the model; adding or removing a file adds or removes an application.
- **Kubernetes**: a k8s-native Custom Resource Definition read via the Kubernetes API, one Custom Resource per application, watched **cluster-wide** and aggregated. Each instance selects the Custom Resources it owns by a matching attribute (a label selector / instance name on the CR), so Custom Resources may live in any namespace and multiple callout instances can coexist on one cluster. The k8s implementation lives in its own assembly so the Kubernetes client dependency stays out of the core.
- **Database**: future.

Exactly one routing/apps source is active at a time; sources are not combined, and there is no environment-variable overlay.

### Canonical model and mapping

The core consumes a single canonical config model (applications, profiles, anonymous, JWT). JSON and YAML deserialize directly into it. Every other source owns its own external shape and a mapping layer into the canonical model (the Kubernetes CRD is idiomatic `apiVersion`/`kind`/`spec`, mapped inside its assembly). The core only ever sees the canonical model.

### Secrets

Sensitive values (`IssuerSeed`, `ServiceUserSeed`, and later a database connection string) are resolvable as secret references (Kubernetes Secret, mounted file, vault) separately from plain config. Inline values remain supported for simple or local use.

## Consequences

- Storage backends are swappable behind one interface; the core is storage-agnostic.
- Add/remove-an-application is a uniform, discrete operation across Kubernetes Custom Resources, files, and later database rows, composing with the anonymous-union model (ADR 0004).
- The Kubernetes watcher brings a client dependency and RBAC, isolated to its own assembly.
- Reload and failure behavior is specified in ADR 0006.
- The cluster-wide watch requires a ClusterRole to read the Custom Resources; the instance-matching attribute keeps one instance from consuming another's Custom Resources.

## Related

- Design: [04 — Configuration](../design/04-configuration.md), [05 — Runtime and lifecycle](../design/05-runtime-and-lifecycle.md).
- ADRs: [0006](./0006-config-reload-and-failure-semantics.md) (reload/failure), [0007](./0007-internal-configuration-model.md) (canonical model each source maps into).
