# nats-auth-callout-service

A minimal, standalone [NATS auth callout](https://docs.nats.io/running-a-nats-service/configuration/securing_nats/auth_callout)
service. It validates a connecting client's OIDC/JWT bearer token and mints a
scoped NATS user JWT with per-application publish/subscribe permissions.

- **Single CLI executable** — no ASP.NET, no background-worker host.
- **Config-driven** — no domain logic baked in; subjects, claims, and scopes
  are all configuration.
- **Multi-application** — one instance serves several apps, each with its own
  OIDC issuer, permissions, and NATS account.
- **Observable** — OpenTelemetry traces, metrics, and logs (OTLP + console).

See [DESIGN.md](DESIGN.md) for architecture and how it was extracted from
`mise`.

## Quick start

```bash
# Build and test
dotnet build -c Release
dotnet test tests/NatsAuthCallout.Tests            # unit
dotnet test tests/NatsAuthCallout.IntegrationTests # integration (needs Docker)

# Run locally (Development config)
DOTNET_ENVIRONMENT=Development dotnet run --project src/NatsAuthCallout.Cli
```

## Docker

```bash
docker build --build-arg VERSION=0.1.0 -t nats-auth-callout .
docker run --rm \
  -e DOTNET_ENVIRONMENT=Production \
  -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
  nats-auth-callout
```

Released images are published to
`ghcr.io/ryandanthony/nats-auth-callout-service` on `v*` git tags.

## Configuration

Configuration is layered: `appsettings.json` →
`appsettings.{DOTNET_ENVIRONMENT}.json` → environment variables → CLI args.
Environment variables use the standard double-underscore mapping, e.g.
`Nats__IssuerSeed`, `Nats__Applications__0__Name`.

```jsonc
{
  "OpenTelemetry": {
    "ServiceName": "nats-auth-callout",
    "Endpoint": "http://localhost:4317", // OTLP; empty disables OTLP export
    "Protocol": "grpc",                  // grpc | httpprotobuf
    "Console": true,                     // console logs
    "ConsoleMetrics": false,             // console metrics/traces (verbose)
    "LogLevel": "Information"
  },
  "Nats": {
    "Url": "nats://localhost:4222",
    "IssuerSeed": "S...",         // AUTH account seed (auth_callout issuer)
    "ServiceUserSeed": "S...",    // callout service's own NATS user
    "DefaultApplication": "mise", // used when routing finds no match
    "Applications": [
      {
        "Name": "mise",
        "TargetAccount": "APP",   // minted-user audience (NATS account)
        "Routing": {
          "ServiceAccountNKeys": ["U..."], // bypass JWT, full access
          "ConnectUsernames": ["mise"],    // route by NATS username
          "TokenIssuers": ["http://localhost:8180/realms/mise"] // route by iss
        },
        "Jwt": {
          "Authority": "http://localhost:8180/realms/mise",
          "Audience": "mise-api",
          "ValidateAudience": true,
          "JwksCacheMinutes": 60
        },
        "Permissions": {
          "Anonymous": {
            "Publish": ["mise.auth.commands", "_INBOX.>"],
            "Subscribe": ["_INBOX.>"],
            "ExpirySeconds": 600
          },
          "Authenticated": {
            "ScopeClaim": "household_id", // expands {scope} below, per value
            "RequireScope": true,
            "NameClaim": "sub",
            "Subscribe": ["mise.events.>", "_INBOX.>"],
            "PublishPerScope": [
              "mise.house.{scope}.commands",
              "mise.house.{scope}.queries"
            ],
            "SubscribePerScope": ["mise.house.{scope}.events.>"],
            "FallbackExpirySeconds": 300
          }
        }
      }
    ]
  }
}
```

### Permission model

| Principal | How it's decided | Grant |
|-----------|------------------|-------|
| Service account | user NKey in `Routing.ServiceAccountNKeys` | full `>` pub/sub |
| Authenticated | valid JWT with the required `ScopeClaim` | static grants + per-scope templated subjects |
| Anonymous | no/invalid token, or missing required scope | `Anonymous` grants only |
| Denied | no application matched and no default | empty grant |

`{scope}` in per-scope templates expands once per value of `ScopeClaim`, so a
token with two `household_id` values gets both households' subjects.

## Project layout

```
src/
  NatsAuthCallout/        # core library (transport-free decision logic + runner)
  NatsAuthCallout.Cli/    # console entry point, config, OpenTelemetry
tests/
  NatsAuthCallout.Tests/             # unit tests
  NatsAuthCallout.IntegrationTests/  # Testcontainers (NATS) + in-process JWKS
```
