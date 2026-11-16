#!/usr/bin/env bash
# Validates deploy/flux offline: Flux kustomize build, then a helm lint + template of the
# chart with each env's exact HelmRelease values. Writes only under .tmp-flux/ (gitignored).
set -euo pipefail
cd "$(dirname "$0")/../.."
out=.tmp-flux
mkdir -p "$out"

cat > "$out/ks.yaml" <<'EOF'
apiVersion: kustomize.toolkit.fluxcd.io/v1
kind: Kustomization
metadata: {name: nats-auth-callout-service, namespace: flux-system}
spec: {path: ./deploy/flux, interval: 10m, prune: true, sourceRef: {kind: GitRepository, name: nats-auth-callout-service}}
EOF

echo "== flux build"
flux build kustomization nats-auth-callout-service --path ./deploy/flux \
  --kustomization-file "$out/ks.yaml" --dry-run > "$out/flux-build.yaml"
grep -E '^kind:' "$out/flux-build.yaml" | sort | uniq -c

for env in test prod; do
  echo "== chart with $env values"
  yq 'select(.kind == "HelmRelease") | .spec.values' "deploy/flux/$env/release.yaml" > "$out/values-$env.yaml"
  helm lint deploy/helm/nats-auth-callout-service -f "$out/values-$env.yaml"
  helm template nacs deploy/helm/nats-auth-callout-service -n "nacs-$env" \
    -f "$out/values-$env.yaml" > "$out/render-$env.yaml"
  grep -E '^kind:' "$out/render-$env.yaml" | sort | uniq -c
done
