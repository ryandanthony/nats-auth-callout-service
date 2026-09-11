# nats-auth-callout-service

[![CI](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/ci.yml/badge.svg)](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/ci.yml)
[![Docs](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/docs.yml/badge.svg)](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/docs.yml)
[![Release](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/release.yml/badge.svg)](https://github.com/ryandanthony/nats-auth-callout-service/actions/workflows/release.yml)

A standalone NATS [auth callout](https://docs.nats.io/running-a-nats-service/configuration/securing_nats/auth_callout)
service written in C# / .NET 10. It intercepts `$SYS.REQ.USER.AUTH` requests,
validates bearer tokens against configured OIDC issuers (JWKS), resolves a
grant (or denial) from a config-driven routing model, and mints signed NATS
user JWTs, so client applications authenticate against an external identity
provider (e.g. Keycloak) instead of static NATS credentials. Routing config
can be sourced from local files, a Kubernetes CRD, or PostgreSQL, and
identity/session state is persisted in NATS JetStream KV.

The service is a single self-contained worker process (no inbound business
HTTP API beyond a minimal `/healthz` `/readyz` `/startupz` surface on port
`8080`), designed to run as one or more instances behind a NATS queue-group
subscription for HA.

**📖 Full documentation: <https://ryandanthony.github.io/nats-auth-callout-service/>**
— getting started, configuration reference, config sources, authorization
model, identity propagation, deployment (Helm + container + cosign
verification), operations, and troubleshooting.

## Quick start

```bash
docker pull ghcr.io/ryandanthony/nats-auth-callout-service:latest
```

```bash
docker run --rm -p 8080:8080 \
  -v $PWD/bootstrap.yaml:/config/bootstrap.yaml:ro \
  -v $PWD/apps:/config/apps:ro \
  ghcr.io/ryandanthony/nats-auth-callout-service:latest
```

See [Getting started](https://ryandanthony.github.io/nats-auth-callout-service/docs/getting-started/overview)
for NATS server configuration, the bootstrap/application file shapes, and
your first connection. Helm chart install: `deploy/helm/nats-auth-callout-service/`
(values reference in the [deployment docs](https://ryandanthony.github.io/nats-auth-callout-service/docs/operations/deployment)).

Images are signed keyless with [cosign](https://github.com/sigstore/cosign)
and carry an SBOM + SLSA provenance attestation — see
[Image verification](https://ryandanthony.github.io/nats-auth-callout-service/docs/operations/deployment#image-verification-cosign).

## Design

[`docs/design/`](./docs/design/) has the full design (start at
[`01-overview.md`](./docs/design/01-overview.md)); the
[architecture decision log](./docs/architecture-decision-log/) records the
rationale behind key decisions. The docs site's guides summarize and link
these for operators/application authors; the design docs and ADRs remain
the authored source of truth. The build is assembled incrementally per
[`implementation-plan/`](./implementation-plan/).

## Build

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/) (version pinned
in `global.json`), Docker (for integration tests via Testcontainers).

### Restore

This repo's analyzer package, `FestinaLente.CodeStandards`, is published to
a GitHub Packages feed under the `festina-lente-io` org (see `nuget.config`).
Restoring requires an authenticated `github` source even though the package
itself is public. Locally, generate a GitHub PAT with `read:packages` scope
and run:

```bash
dotnet nuget update source github -u <your-github-username> -p <your-pat> --store-password-in-clear-text
```

Then:

```bash
git clone https://github.com/ryandanthony/nats-auth-callout-service.git
cd nats-auth-callout-service
dotnet restore AuthCallout.slnx
dotnet build AuthCallout.slnx -warnaserror
dotnet test AuthCallout.slnx
```

CI authenticates the same feed using the workflow's `GITHUB_TOKEN`; see
[`.github/workflows/ci.yml`](./.github/workflows/ci.yml).

## Local dev loop

Two [.NET Aspire](https://aspire.dev) AppHosts under [`dev/`](./dev) spin up
NATS + Keycloak + the callout + two sample services with one command
(`dotnet run --project dev/AppHost.Local`) — see [`dev/README.md`](./dev/README.md).

## Documentation site source

The docs site itself (Docusaurus, matching [ryandanthony/dottie](https://github.com/ryandanthony/dottie)'s
stack) lives in [`docs-site/`](./docs-site/); see
[`docs-site/website/README.md`](./docs-site/website/README.md) for building
it locally.
