# P16 — Container, release CI, supply chain

**Wave:** 1 (can start right after P01; finalizes after P13 exists)
**Depends on:** P01 (skeleton); final validation needs P13
**Blocks:** P17, M3
**Estimated size:** 1 agent-day

## Scope

Dockerfile, release workflow (GitVersion → tag → GHCR → GitHub Release),
cosign signing, SBOM + provenance — doc 08 §Container & supply chain,
matching the existing GHCR conventions (`mcp-private-library` pattern:
tag-based CI, `v*` tag → image + release, no auto-deploy).

## Files owned

```
Dockerfile
.dockerignore
.github/workflows/release.yml
```

(`ci.yml` belongs to P01; changes coordinator-mediated.)

## Tasks

1. **Dockerfile** per doc 08: multi-stage, SDK 10.0 build →
   `mcr.microsoft.com/dotnet/runtime:10.0` final; publish
   `AuthCallout.Host` trimmed (`PublishTrimmed=true`,
   `InvariantGlobalization=true`, `TrimmerRootAssembly`/`DynamicDependency`
   for the config-binding model as needed — coordinate with P13 on what
   reflection survives trimming; **not** Native AOT). Non-root
   (`USER $APP_UID`), no shell in final stage, `EXPOSE 8080`,
   `linux-x64` + `linux-arm64`.
2. **Release workflow** (`v*` tag): GitVersion computes the version,
   `docker/build-push-action` multi-arch push to
   `ghcr.io/ryandanthony/nats-auth-callout-service`, GitHub Release with
   notes. `--sbom=true --provenance=true` on buildx.
3. **Signing**: cosign keyless (OIDC, `id-token: write`), sign the
   **digest**; attach SBOM attestation. Document the
   `cosign verify` / `verify-attestation` commands in the README (consumed
   by P18).
4. Trim-safety check in CI: a smoke step that runs the published trimmed
   binary with `--help`/health boot against a config fixture, so trim
   breakage is caught pre-release (add once P13 lands).

## Definition of done

- [ ] `docker build` succeeds locally; image boots and serves `/healthz`
      (after P13; before that, DoD is build-only, revisit).
- [ ] Release workflow green on a `v0.0.x` prerelease tag; image signed,
      SBOM + provenance attached, `cosign verify` documented and tested.
- [ ] Image size and trim warnings reviewed (no suppressed trim warnings
      without a comment).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
