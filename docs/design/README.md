# Design — nats-auth-callout-service

A cohesive, language-agnostic design for the NATS auth callout service, unifying the decisions in [../architecture-decision-log](../architecture-decision-log). This describes *what the system is and how its parts fit together*, not how to build it or in what language.

## Purpose

The service is a [NATS auth callout](https://docs.nats.io/running-a-nats-service/configuration/securing_nats/auth_callout) responder. When a client connects to NATS, the server delegates the authorization decision to this service over `$SYS.REQ.USER.AUTH`. The service authenticates the client's OIDC/JWT bearer token, decides what the client may publish and subscribe to, and returns a signed, scoped NATS user JWT.

It is generic and data-driven: no application-specific subjects or claims are compiled in. One running instance serves many applications, each with its own issuer, permission model, and NATS account.

## Design documents

| Document | Covers |
|----------|--------|
| [01-overview.md](./01-overview.md) | Context, responsibilities, component map, request lifecycle |
| [02-authorization-model.md](./02-authorization-model.md) | Applications, profiles, claim matching, templated subject variables, identity propagation, anonymous union, the decision tree |
| [03-resolution-pipeline.md](./03-resolution-pipeline.md) | Issuer → validate → app → profile, grant construction, token store, event emission |
| [04-configuration.md](./04-configuration.md) | Two config layers, sources, canonical model, secrets |
| [05-runtime-and-lifecycle.md](./05-runtime-and-lifecycle.md) | State machine, reload, failure handling, identity stores, observability, events |
| [06-identity-propagation-sequence.md](./06-identity-propagation-sequence.md) | End-to-end sequence: how client, subscriber, callout, and NATS interact so a subscriber can obtain the client's OIDC JWT |
| [07-kv-storage.md](./07-kv-storage.md) | Authoritative reference for the KV buckets (token store, id lookup, assertion keys): keys, values, writers, readers, TTLs |
| [09-schemas-and-versioning.md](./09-schemas-and-versioning.md) | Concrete schemas for events, discovery descriptor, config files (JSON/YAML), and the Kubernetes CRD, plus the versioning strategy for each |

The above are language-agnostic. One implementation-specific doc maps them to a concrete stack:

| Document | Covers |
|----------|--------|
| [08-technical-architecture-csharp.md](./08-technical-architecture-csharp.md) | C# / .NET 10 stack: NATS.Net + NATS.Jwt/NKeys, KubernetesClient CRD watch, OIDC/JWKS validation, project layout, and open gaps to decide |

## Grounding decisions

Every element traces to an ADR (see the [ADR log](../architecture-decision-log/README.md)):

- [ADR 0001](../architecture-decision-log/0001-named-permission-profiles.md) — named permission profiles per application
- [ADR 0002](../architecture-decision-log/0002-service-auth-via-oidc-not-known-keys.md) — service-to-service auth via OIDC, not known keys
- [ADR 0003](../architecture-decision-log/0003-resolution-order-issuer-validate-app-profile.md) — resolution order: issuer, validate, app match, profile match
- [ADR 0004](../architecture-decision-log/0004-anonymous-grant-is-union-across-apps.md) — anonymous grant is the union across applications
- [ADR 0005](../architecture-decision-log/0005-config-abstraction-and-sources.md) — config abstraction with pluggable sources
- [ADR 0006](../architecture-decision-log/0006-config-reload-and-failure-semantics.md) — config reload and failure semantics
- [ADR 0007](../architecture-decision-log/0007-internal-configuration-model.md) — internal configuration model
- [ADR 0008](../architecture-decision-log/0008-publish-auth-outcome-events.md) — publish auth outcome events per application
- [ADR 0009](../architecture-decision-log/0009-templated-subject-variables.md) — templated subject variables bound to transformed claims
- [ADR 0010](../architecture-decision-log/0010-session-jwt-store-in-kv.md) — callout-maintained token store keyed by a generated id
- [ADR 0011](../architecture-decision-log/0011-signed-identity-assertion.md) — callout-signed identity assertion (sole identity-propagation mechanism)

## Principles

- **Data-driven.** Subjects, claims, scopes, and routing are configuration, not code.
- **Token-anchored trust.** Every presented token is fully validated; only a fully validated token that lands on a profile is granted anything. No key allow-lists or bypasses.
- **Fail closed.** Ambiguity, misconfiguration, and invalid tokens deny. The service runs only on fully valid config, or nothing.
- **Isolation.** Applications are independent: a token for one can never gain another's subjects, and each has its own account and audit stream.
- **Composable.** Adding or removing an application is a discrete config change that also adds or removes its anonymous contribution and its event stream.
- **Provider-neutral.** Standard OIDC/JWT only (discovery, JWKS, configurable claims); no identity provider is hard-coded. Keycloak appears only as an example.
