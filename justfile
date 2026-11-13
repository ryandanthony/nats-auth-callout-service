# Justfile for nats-auth-callout-service
# Run `just` or `just --list` to see available recipes.

# How AppHost.Local runs the callout (its `--callout` flag, see dev/AppHost.Local/AppHost.cs):
# `docker` builds and runs the repo's Dockerfile image (the shipped self-contained ReadyToRun
# binary), `local` runs the AuthCallout.Host project from the working tree. Every aspire
# recipe below takes it as an optional argument, e.g. `just run-aspire-local local`.
callout := "docker"

# Show available recipes
default:
    @just --list

# Build the solution
build:
    dotnet build AuthCallout.slnx

# Build the solution in Release (used by reset-aspire-local-release: `build` always builds
# Debug regardless of what dotnet run -c Release is about to do next, so
# reset-aspire-local-release must depend on this, not on `build`, or the AppHost and every
# project resource it launches - callout, load, note, task, all inherit its configuration,
# see run-aspire-local-release below - would still bind against stale Debug DLLs first).
build-release:
    dotnet build AuthCallout.slnx -c Release

# Run the tests
test:
    dotnet test AuthCallout.slnx

# Run the callout's real-NATS latency-goal benchmark/regression gate (p50<=25ms, p99<=50ms).
# Requires Docker (Testcontainers); exits non-zero if the goal is not met.
bench:
    dotnet run --project tests/AuthCallout.Benchmarks -c Release

# Start the local Aspire AppHost (files-source callout + NATS + Keycloak + samples).
# Docker mode's image build restores FestinaLente.CodeStandards from GitHub Packages, so it
# passes NUGET_GITHUB_TOKEN (falling back to `gh auth token`) as the Dockerfile's BuildKit secret.
run-aspire-local mode=callout:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "{{ mode }}" = "docker" ] && [ -z "${NUGET_GITHUB_TOKEN:-}" ]; then
        export NUGET_GITHUB_TOKEN="$(gh auth token)"
    fi
    exec dotnet run --project dev/AppHost.Local -- --callout "{{ mode }}"

# Start the local Aspire AppHost in Release. Aspire's DCP executor reads the AppHost's own
# build configuration and forwards it as `--configuration <config>` to every project
# resource it launches (callout, load, note, task all inherit it) - there is no separate
# per-resource "release mode" switch, running the AppHost itself in Release is the switch.
# Use this to rule out Debug JIT/tiering warm-up cost when investigating latency (doc 08).
# In docker mode the callout image is always a Release publish regardless; this then only
# changes the samples/load generator.
run-aspire-local-release mode=callout:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "{{ mode }}" = "docker" ] && [ -z "${NUGET_GITHUB_TOKEN:-}" ]; then
        export NUGET_GITHUB_TOKEN="$(gh auth token)"
    fi
    exec dotnet run --project dev/AppHost.Local -c Release -- --callout "{{ mode }}"

# Stop any running local Aspire AppHost (its session-lifetime NATS/Keycloak containers are torn down on exit)
# The 'AppHost[.]Local' regex matches the real processes but not this pkill's own command line (the
# bracket keeps the pattern from matching itself), avoiding the classic pkill-self kill.
stop-aspire-local:
    -pkill -f 'AppHost[.]Local' 2>/dev/null || true
    @sleep 3
    @if pgrep -f 'AppHost[.]Local' >/dev/null 2>&1; then echo "AppHost still running; sending SIGKILL"; pkill -9 -f 'AppHost[.]Local' || true; sleep 2; fi
    @echo "AppHost.Local stopped."

# Reset the local Aspire AppHost: stop it, rebuild, then start fresh
reset-aspire-local mode=callout: stop-aspire-local build (run-aspire-local mode)

# Reset the local Aspire AppHost in Release: stop it, rebuild, then start fresh in Release
reset-aspire-local-release mode=callout: stop-aspire-local build-release (run-aspire-local-release mode)
