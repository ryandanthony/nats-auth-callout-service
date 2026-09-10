# ADR index — nats-auth-callout-service

Architecture Decision Records for `nats-auth-callout-service`. Each ADR captures one decision, its context, and consequences.

Status legend: `proposed` (agreed, pre-implementation), `accepted` (locked in), `superseded`.

The [design](../design/README.md) unifies these ADRs into a cohesive, language-agnostic design.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](./0001-named-permission-profiles.md) | Named permission profiles per application | proposed |
| [0002](./0002-service-auth-via-oidc-not-known-keys.md) | Service-to-service auth via OIDC, not known keys | proposed |
| [0003](./0003-resolution-order-issuer-validate-app-profile.md) | Resolution order: issuer, validate, app Match, profile Match | proposed |
| [0004](./0004-anonymous-grant-is-union-across-apps.md) | Anonymous grant is the union across applications | proposed |
| [0005](./0005-config-abstraction-and-sources.md) | Config abstraction with pluggable sources | proposed |
| [0006](./0006-config-reload-and-failure-semantics.md) | Config reload and failure semantics | proposed |
| [0007](./0007-internal-configuration-model.md) | Internal configuration model | proposed |
| [0008](./0008-publish-auth-outcome-events.md) | Publish auth outcome events per application | proposed |
| [0009](./0009-templated-subject-variables.md) | Templated subject variables bound to transformed claims | proposed |
| [0010](./0010-session-jwt-store-in-kv.md) | Callout-maintained token store keyed by a generated id | proposed |
| [0011](./0011-signed-identity-assertion.md) | Callout-signed identity assertion (sole identity-propagation mechanism) | proposed |
| [0012](./0012-identity-propagation-toggle-and-discovery.md) | Per-application identity-propagation toggle and discovery descriptor | proposed |
| [0013](./0013-kv-bucket-naming-contract.md) | KV bucket naming contract | proposed |
| [0014](./0014-key-rotation-orchestration.md) | Key rotation orchestration | proposed |
| [0015](./0015-schema-versioning-strategy.md) | Schema versioning strategy | proposed |
| [0016](./0016-token-validation-parameters.md) | Token validation parameters | proposed |
| [0017](./0017-watch-loss-and-connection-loss-semantics.md) | Watch-loss and connection-loss semantics | proposed |
| [0018](./0018-xkey-encrypted-callout.md) | xkey-encrypted callout | proposed |
