# syntax=docker/dockerfile:1
# Multi-stage build for AuthCallout.Host (doc 08 §Container & supply chain, P16).
#
# Framework-dependent, self-contained, ReadyToRun on the dotnet/runtime base (worker +
# minimal-API health only, no ASP.NET/MVC/Razor needed). Not full Native AOT: NKeys/crypto
# and reflection-based config binding (System.Text.Json + options) make full AOT fragile
# (doc 08 §Runtime & trimming). Not trimmed (PublishTrimmed) either, despite that having once
# been the plan here: a real trimmed+R2R publish (verified locally, not just assumed) failed
# with IL2026/IL2070/etc trim-analysis errors inside three third-party dependencies this
# service pulls in transitively - Dapper (SqlMapper builds its object-materialization IL via
# reflection, its whole ORM mechanism), KubernetesClient (reflection-based kubeconfig/YAML
# loading), and YamlDotNet (arbitrary-object-graph deserialization via reflection) - none of
# which this repo owns or can annotate its way out of without forking them. Suppressing those
# warnings would risk silently broken reflection at runtime on any code path the test suite
# doesn't happen to exercise, which is a worse failure mode than a larger image. ReadyToRun
# stays on its own: it pre-JITs IL to native code as an addition alongside the IL (JIT remains
# the fallback for everything, including all the reflection above), so it carries none of
# trimming's static-analysis risk, and it directly targets the cold-start JIT/tiering cost
# observed on a fresh instance's first live request (~200ms here vs ~5ms steady-state, doc 08
# §Latency) at the cost of a larger image (native code alongside the IL).

# ---- Build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
# TARGETARCH is set automatically by buildx for the platform being built
# (amd64/arm64); mapped below to the matching .NET RID.
ARG TARGETARCH

# NuGet auth for the festina-lente-io GitHub Packages feed (FestinaLente.CodeStandards),
# matching the CI convention in .github/workflows/ci.yml. Passed as a BuildKit secret
# (not ARG/ENV) so the token never lands in an image layer or build cache metadata; the
# secret file only exists for the duration of the RUN step that mounts it, and no secret
# material ends up in the final image regardless (multi-stage COPY --from=build below
# copies only the publish output, never NuGet.Config or credentials).
COPY nuget.config ./
RUN --mount=type=secret,id=nuget_github_token \
    if [ -f /run/secrets/nuget_github_token ]; then \
        dotnet nuget update source github \
            --username "github-actions" \
            --password "$(cat /run/secrets/nuget_github_token)" \
            --store-password-in-clear-text \
            --configfile nuget.config; \
    fi

# Restore first for better layer caching: copy only solution/project metadata, not source.
# Restore is RID-aware (-r) since the project publishes self-contained; an RID-less restore
# would not produce the linux-x64/linux-arm64 assets `dotnet publish -r` needs later.
COPY AuthCallout.slnx ./
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props stylecop.json global.json ./
COPY src/AuthCallout.Contracts/AuthCallout.Contracts.csproj src/AuthCallout.Contracts/
COPY src/AuthCallout.Core/AuthCallout.Core.csproj src/AuthCallout.Core/
COPY src/AuthCallout.Secrets/AuthCallout.Secrets.csproj src/AuthCallout.Secrets/
COPY src/AuthCallout.Identity/AuthCallout.Identity.csproj src/AuthCallout.Identity/
COPY src/AuthCallout.Nats/AuthCallout.Nats.csproj src/AuthCallout.Nats/
COPY src/AuthCallout.Config.Files/AuthCallout.Config.Files.csproj src/AuthCallout.Config.Files/
COPY src/AuthCallout.Config.K8s/AuthCallout.Config.K8s.csproj src/AuthCallout.Config.K8s/
COPY src/AuthCallout.Config.Postgres/AuthCallout.Config.Postgres.csproj src/AuthCallout.Config.Postgres/
COPY src/AuthCallout.Host/AuthCallout.Host.csproj src/AuthCallout.Host/
RUN case "$TARGETARCH" in \
        amd64) RID=linux-x64 ;; \
        arm64) RID=linux-arm64 ;; \
        *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && echo "$RID" > /tmp/rid \
    && dotnet restore src/AuthCallout.Host/AuthCallout.Host.csproj -r "$RID" -p:PublishReadyToRun=true

# Copy the rest and publish, self-contained, for the target architecture. DisableGitVersionTask
# skips GitVersion.MsBuild's own git-history-based version computation (it needs .git, which
# .dockerignore deliberately excludes from the build context) since CI already resolved the
# version and passes it in via VERSION/INFORMATIONAL_VERSION build args.
COPY . .
RUN RID="$(cat /tmp/rid)" \
    && dotnet publish src/AuthCallout.Host/AuthCallout.Host.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        -r "$RID" \
        --self-contained true \
        -p:Version=${VERSION} \
        -p:InformationalVersion=${INFORMATIONAL_VERSION} \
        -p:DisableGitVersionTask=true \
        -p:InvariantGlobalization=true \
        -p:PublishReadyToRun=true

# ---- Runtime stage ----------------------------------------------------------
# Base runtime image (not aspnet): AuthCallout.Host is a Worker service with a minimal
# health-check listener, not an ASP.NET MVC/Razor app (doc 08 §Runtime & trimming).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

ARG VERSION=0.0.0
LABEL org.opencontainers.image.title="nats-auth-callout-service" \
      org.opencontainers.image.description="NATS auth callout service: OIDC-token-gated NATS connect authorization" \
      org.opencontainers.image.source="https://github.com/ryandanthony/nats-auth-callout-service" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.licenses="MIT"

COPY --from=build /app/publish ./

# Non-root, no shell (the base runtime image has no shell in its final layer already;
# USER $APP_UID is the documented dotnet-base convention for the non-root numeric UID).
USER $APP_UID

ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080

ENTRYPOINT ["./AuthCallout.Host"]
