# Justfile for nats-auth-callout-service
# Run `just` or `just --list` to see available recipes.

# Show available recipes
default:
    @just --list

# Build the solution
build:
    dotnet build AuthCallout.slnx

# Run the tests
test:
    dotnet test AuthCallout.slnx

# Start the local Aspire AppHost (files-source callout + NATS + Keycloak + samples)
run-aspire-local:
    dotnet run --project dev/AppHost.Local

# Stop any running local Aspire AppHost (its session-lifetime NATS/Keycloak containers are torn down on exit)
# The 'AppHost[.]Local' regex matches the real processes but not this pkill's own command line (the
# bracket keeps the pattern from matching itself), avoiding the classic pkill-self kill.
stop-aspire-local:
    -pkill -f 'AppHost[.]Local' 2>/dev/null || true
    @sleep 3
    @if pgrep -f 'AppHost[.]Local' >/dev/null 2>&1; then echo "AppHost still running; sending SIGKILL"; pkill -9 -f 'AppHost[.]Local' || true; sleep 2; fi
    @echo "AppHost.Local stopped."

# Reset the local Aspire AppHost: stop it, rebuild, then start fresh
reset-aspire-local: stop-aspire-local build run-aspire-local
