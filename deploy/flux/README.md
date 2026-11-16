# NACS on the shared app cluster (Flux)

GitOps deployment of two NACS instances to the home k3s cluster (`apps-node01`), each the
auth callout for one NATS server on the NAS. Both use the **Kubernetes config source**: apps
ship their routing config as `AuthApplication` custom resources from their own repos, and
NACS hot-reloads them. No app config lives here or on the NAS.

| | test | prod |
|---|---|---|
| Namespace / release | `nacs-test` / `nacs` | `nacs-prod` / `nacs` |
| NATS (TLS-first) | `tls://apps-test.ants.zone:4222` | `tls://apps.ants.zone:4222` |
| Claims CRs labelled | `authcallout.ants.zone/owner: apps-test` | `authcallout.ants.zone/owner: apps` |
| Version | newest published chart (tracks main) | newest published chart (tracks main) |

## Layout

```
crds/          the shared AuthApplication CRD (from ../crd/), applied once for both envs
namespaces.yaml  nacs-test, nacs-prod + ResourceQuota/LimitRange
test/ prod/    OCIRepository + HelmRelease per env (chart oci://ghcr.io/ryandanthony/charts/nats-auth-callout-service)
onboarding/    one-time GitRepository + Kustomization (applied by hand, not reconciled)
probe/         token-free callout probe (+ optional authenticated checks)
validate.sh    offline: flux build + helm lint/template with each env's HelmRelease values
```

The chart's own CRD install is off (`source.k8s.crds.install: false`): two releases cannot
both own one cluster-scoped object. Each release gets a unique `fullnameOverride` because the
chart's ClusterRole/ClusterRoleBinding are cluster-scoped and named after it.

## Onboarding an app

Add an `AuthApplication` CR to the app's own Flux tree, in the app's namespace, with the
env's owner label. `metadata.name` is the application name; `spec` is the same shape as an
application file minus `schemaVersion`/`name`. Check that NACS accepted it:

```bash
kubectl --context apps get authapplications -A -L authcallout.ants.zone/owner
```

NACS rejects unknown fields and writes `accepted`/`reason` to `.status`. Mise is the first
example (`ryandanthony/mise` `deploy/flux/{test,prod}/nacs-application.yaml`).

## Secrets (out of band, never in git)

This repo is public, so seeds are plain Secrets created by hand in each namespace:
`nacs-issuer`, `nacs-service`, `nacs-assertion` (key `seed`), plus `ghcr-pull`. The source of
truth is the NAS seed files (`~/services/nats/secrets/<instance>.{issuer,service,assertion}.seed`,
whose public keys the NATS confs trust). Recreate one without printing it:

```bash
ssh -p 2222 nas.ants.zone "sudo -n cat /mnt/dbpool/homes/ryan/services/nats/secrets/apps-test.issuer.seed" \
  | tr -d '\n' | kubectl --context apps -n nacs-test create secret generic nacs-issuer \
      --from-file=seed=/dev/stdin --dry-run=client -o yaml | kubectl --context apps apply -f -
```

## Verify

```bash
./validate.sh                                             # offline
dotnet run --project probe -- apps-test                   # live, token-free (6 checks)
NACS_PROBE_TOKEN=<household user token> dotnet run --project probe -- apps-test   # +5
```

The probe passes 6/6 against a healthy callout and fails with `Authorization Violation` when
no callout answers. Onboarding: `kubectl apply -f onboarding/`.
