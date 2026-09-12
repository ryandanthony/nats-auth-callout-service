# Justfile for nats-auth-callout-service
# Run `just` or `just --list` to see available recipes.

# Show available recipes
default:
    @just --list

# Start the local Aspire AppHost (files-source callout + NATS + Keycloak + samples)
run-aspire-local:
    dotnet run --project dev/AppHost.Local
