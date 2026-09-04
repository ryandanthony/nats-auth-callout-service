# syntax=docker/dockerfile:1

# ---- build -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Version is supplied by CI (from the git tag); GitVersion is disabled in-image
# since the .git directory is not part of the build context.
ARG VERSION=0.0.0-dev

COPY . .

RUN dotnet restore src/NatsAuthCallout.Cli/NatsAuthCallout.Cli.csproj \
    -p:DisableGitVersion=true
RUN dotnet publish src/NatsAuthCallout.Cli/NatsAuthCallout.Cli.csproj \
    -c Release -o /app --no-restore \
    -p:DisableGitVersion=true \
    -p:Version=${VERSION}

# ---- runtime ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Run as a non-root user.
RUN useradd --system --uid 1001 --create-home appuser
USER appuser

COPY --from=build /app .

ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "nats-auth-callout.dll"]
