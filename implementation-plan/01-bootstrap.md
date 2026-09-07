# P01 — Bootstrap (repo, solution, conventions, CI)

**Wave:** 0 (first packet, everything depends on it)
**Depends on:** nothing
**Blocks:** everything
**Estimated size:** 0.5–1 agent-day

## Scope

Create the committed skeleton every other packet builds inside: solution,
empty projects, build conventions, pinned packages, CI. After this packet, any
worker can `git pull && dotnet build && dotnet test` green.

Grounding: `docs/design/08-technical-architecture-csharp.md` (project layout,
stack table), QUESTIONS.md B5–B9.

## Files owned

```
*.sln, global.json, .gitignore, .editorconfig
Directory.Build.props, Directory.Build.targets, Directory.Packages.props
stylecop.json, GitVersion.yml
.github/workflows/ci.yml
src/AuthCallout.Host/            (Program.cs shell only)
src/AuthCallout.Core/
src/AuthCallout.Contracts/       (empty project shell; P02 fills it)
src/AuthCallout.Nats/
src/AuthCallout.Identity/
src/AuthCallout.Config.Files/
src/AuthCallout.Config.K8s/
src/AuthCallout.Config.Postgres/
src/AuthCallout.Secrets/
tests/AuthCallout.Core.Tests/
tests/AuthCallout.Contracts.Tests/
tests/AuthCallout.Nats.Tests/
tests/AuthCallout.Identity.Tests/
tests/AuthCallout.Config.Tests/
tests/AuthCallout.Secrets.Tests/
tests/AuthCallout.Integration/   (project shell; P14 fills it)
```

Note: doc 08's layout lacks `Config.Postgres`; it is added per QUESTIONS A2.
After this packet lands, shared root files (`Directory.Packages.props`, sln,
CI workflow) become **coordinator-mediated** — workers request changes via DM.

## Tasks

1. **Seed conventions from reference repos** (fetch raw files, then adapt):
   - `.editorconfig`, `global.json`, `Directory.Build.props`,
     `Directory.Build.targets`, `stylecop.json` from
     `github.com/ryandanthony/mise` (main branch).
   - `Directory.Packages.props` from mise, **stripped of
     Marten/Wolverine/Avalonia** entries, repopulated with this project's set.
   - `GitVersion.yml` from `github.com/ryandanthony/mcp-private-library`.
   - Reference the `festina-lente-io/code-standards` NuGet package in
     `Directory.Build.props` (verify the exact package id/feed; if it is not
     on nuget.org, ask coordinator for the feed before guessing).
   - `global.json`: pin an SDK available locally (10.0.302 is installed) and
     in CI; set `rollForward: latestFeature`.
2. **Solution + projects** per the layout above. `net10.0`, nullable,
   `TreatWarningsAsErrors`, implicit usings. `AuthCallout.Core` and
   `AuthCallout.Contracts` must reference **no** transport/storage/k8s
   packages (doc 08 invariant). Project references mirror doc 08's process
   composition: Host → everything; Nats/Identity/Config.*/Secrets →
   Contracts (+ Core where noted); Core → Contracts only.
3. **Pin packages** (central package management, exact versions resolved at
   implementation time — record chosen versions in the commit message):
   `NATS.Net`, `NATS.Jwt`, `NATS.NKeys`, `KubernetesClient`,
   `Microsoft.IdentityModel.Protocols.OpenIdConnect`,
   `System.IdentityModel.Tokens.Jwt`, `YamlDotNet`, `Dapper`, `Npgsql`,
   `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http.Resilience`,
   OpenTelemetry (`.Extensions.Hosting`, `.Exporter.OpenTelemetryProtocol`),
   `Microsoft.Extensions.Diagnostics.HealthChecks`, xUnit, `coverlet.collector`,
   `NSubstitute`, `Testcontainers` (+ `.Keycloak`, `.PostgreSql`, `.K3s`),
   `Alba` (host-level tests for the health endpoints).
4. **CI workflow** `.github/workflows/ci.yml`: on PR + push to main —
   checkout, setup-dotnet from `global.json`, `dotnet build -warnaserror`,
   `dotnet test` (unit projects) with coverlet, enforce **≥ 90% line coverage
   on AuthCallout.Core** (threshold fails the build once Core has code; use
   a coverage gate that no-ops on the empty skeleton), then a separate
   `integration` job running `tests/AuthCallout.Integration` (Testcontainers;
   `ubuntu-latest` has Docker). Integration job may be `continue-on-error:
   false` from day one but the project is empty until P14.
5. **README.md** stub: what the service is (2 paragraphs), link to
   `docs/design/`, build instructions.

## Definition of done

- [ ] `dotnet build -warnaserror` and `dotnet test` green locally and in CI.
- [ ] `dotnet format --verify-no-changes` clean.
- [ ] CI runs on push to main; badge in README.
- [ ] All projects compile with the doc 08 reference-isolation invariant
      (verify: `Core`/`Contracts` csproj contain no NATS/k8s/DB packages).
- [ ] Chosen package versions recorded (commit message or `docs/` note).
- [ ] Coordinator notified (unblocks P02 and P16).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
