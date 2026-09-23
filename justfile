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
