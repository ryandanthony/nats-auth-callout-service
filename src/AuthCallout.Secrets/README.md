# AuthCallout.Secrets

Implements `ISecretResolver` (`AuthCallout.Contracts.Seams`) for the four
`SecretRef` kinds defined in `docs/design/04-configuration.md` §Secrets
(ADR 0005): `inline`, `file`, `k8sSecret`, `vault`.

Resolution is a **startup-time-only** concern — the bootstrap layer is never
hot-reloaded (doc 04 §Two layers) — so `SecretResolver` does no caching,
retry, or rotation. Each call reads fresh and returns.

## Kinds

| Kind | Behavior |
|------|----------|
| `inline` | Returns the literal value from the ref. Dev/local use only; resolving one invokes the optional `onWarning` callback (never with the value itself). |
| `file` | Reads the mounted file at `SecretRef.Path`, trims exactly one trailing `\n` or `\r\n`. Missing/unreadable file throws `SecretResolutionException` naming the path, never the value. |
| `k8sSecret` | Reads via the **mounted-volume convention** below — not the Kubernetes API. This assembly takes **no `KubernetesClient` dependency** and needs no RBAC. |
| `vault` | Always throws `NotSupportedException` (doc 08: "File + k8sSecret first; vault behind the same interface"). Registered as a kind, not implemented in v1. |

## k8sSecret mount contract (consumed by P17 / Helm)

A `SecretRef.FromK8sSecret(name, key)` resolves to the file:

```
{k8sSecretMountRoot}/{name}/{key}
```

`k8sSecretMountRoot` defaults to `SecretResolver.DefaultK8sSecretMountRoot`
(`/var/run/secrets`), overridable via the `SecretResolver` constructor. This
mirrors how Kubernetes itself projects a Secret volume mount — one file per
key, named after the key, inside a directory named after the Secret — the
same layout `kubelet` already uses for the built-in service account token at
`/var/run/secrets/kubernetes.io/serviceaccount/`.

**What P17's Helm chart must do** for every `SecretRef` with
`kind: k8sSecret` referenced anywhere in the bootstrap config (issuer seed,
service user seed, assertion keys, xkey seed, Postgres connection string,
...):

1. For each distinct Secret `name` referenced, add a Pod volume of type
   `secret` sourcing that Kubernetes Secret (`secretName: {name}`,
   supporting `existingSecret` values so the chart never generates key
   material itself).
2. Mount that volume at `/var/run/secrets/{name}` (read-only).
3. Each key inside the Secret then appears as
   `/var/run/secrets/{name}/{key}` inside the container, exactly as
   `SecretResolver` expects — no further container-level configuration (no
   env vars, no RBAC, no ServiceAccount token) is required for this kind.

Example: bootstrap config's
`serviceUserSeed: { kind: k8sSecret, name: callout-svc, key: seed }` (doc 09
bootstrap file example) requires a Secret volume named after the Kubernetes
Secret `callout-svc`, mounted at `/var/run/secrets/callout-svc`, so the file
`/var/run/secrets/callout-svc/seed` exists in the container.

## Hygiene

Resolved values are wrapped in `AuthCallout.Contracts.Seams.ResolvedSecret`
(frozen Contracts type, P02): `ToString()` and default string interpolation
always render `[redacted]`; the only way to obtain the plaintext is the
explicit `Reveal()` call. `ResolvedSecret` implements `IDisposable` and
best-effort zeroes its internal buffer on `Dispose()`; `Reveal()` throws
`ObjectDisposedException` afterward.
