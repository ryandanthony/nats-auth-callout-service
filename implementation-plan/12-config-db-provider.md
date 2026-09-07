# P12 — PostgreSQL config provider (Dapper)

**Wave:** 1
**Depends on:** P02 (M0 gate; source-settings shape agreed pre-freeze)
**Blocks:** P14 (its integration tests)
**Estimated size:** 1–1.5 agent-days

## Scope

`IRoutingConfigProvider` over PostgreSQL + Dapper (`AuthCallout.Config.Postgres`).
The design docs marked this "future" (ADR 0005: "rows map into the same
application aggregate; no core change needed"); QUESTIONS A2 pulls it into
scope. This packet defines the storage shape — keep it minimal and
document-oriented rather than a wide relational model.

Grounding: ADR 0005 (provider contract), ADR 0006 (atomic batch), ADR 0017
(staleness applies to polling too), doc 04 (aggregation model: discrete
application units), doc 09 (application document shape).

## Files owned

```
src/AuthCallout.Config.Postgres/**
tests/AuthCallout.Config.Tests/Postgres/**   (unit: mapping)
db/migrations/**                              (SQL migration scripts)
```

## Design (proposed, confirm with coordinator before building)

One table, application documents as JSONB — reusing the **exact file schema
v1** (doc 09 §3) as the document shape so validation, mapping, and golden
tests are shared with P10:

```sql
CREATE TABLE auth_applications (
  name          text PRIMARY KEY,
  schema_version integer NOT NULL DEFAULT 1,
  spec          jsonb NOT NULL,          -- doc 09 application document
  enabled       boolean NOT NULL DEFAULT true,
  updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE FUNCTION notify_auth_applications() ... -- NOTIFY 'auth_applications_changed'
CREATE TRIGGER ... AFTER INSERT OR UPDATE OR DELETE ...
```

Rationale: a normalized many-table model would duplicate the canonical model
in SQL and drift; JSONB + the existing strict deserializer gives one shape,
one validator (ADR 0007 spirit). Migrations are plain ordered `.sql` applied
by the provider at startup (optional `migrateOnStart` setting) or manually.

## Tasks

1. Migration scripts + a tiny migration runner (ordered, recorded in a
   `schema_migrations` table; no heavyweight framework).
2. **`PostgresRoutingProvider`** (Dapper + Npgsql):
   - Load: `SELECT` all enabled rows, deserialize each `spec` with the
     **same strict deserializer as P10** (share it via the Contracts-adjacent
     mapping layer — coordinate placement), aggregate, validate, emit batch.
     Any bad row rejects the batch naming the row (ADR 0006).
   - Change detection: `LISTEN` on the notify channel + periodic poll
     fallback (`pollInterval` from source settings) — the poll doubles as
     `LastSuccessfulSync` heartbeat (ADR 0017 staleness).
   - Connection loss: Polly reconnect with capped backoff; staleness
     surfaces via provider health.
   - `connectionString` arrives as a `SecretRef` (bootstrap), resolved by
     P03 at startup (P13 wires this).
3. Unit tests: mapping + batch semantics against a repository seam;
   **real-Postgres tests live in P14** (Testcontainers.PostgreSql) — provide
   the fixtures.

## ADR→test map

| Behavior (source) | Test |
|-------------------|------|
| Row spec → identical canonical model as equivalent file (ADR 0007) | shared golden contract test |
| Bad row rejects batch, names row (ADR 0006) | unit |
| enabled=false row excluded | unit |
| NOTIFY triggers re-load; poll fallback works with LISTEN unavailable | P14 integration |
| LastSuccessfulSync advances on unchanged polls (ADR 0017) | unit |
| Migration runner idempotent | P14 integration |

## Definition of done

- [ ] Schema + provider design confirmed by coordinator (pre-freeze for the
      source-settings shape; table shape may land post-freeze).
- [ ] Unit tests green; fixtures handed to P14.
- [ ] Doc note added to `docs/design/04-configuration.md` §Sources updating
      "Database (future)" → implemented shape (small PR-sized edit,
      coordinator-approved since it touches shared docs).
- [ ] **Coordinator review passed** (acceptance criteria + ADR→test map verified
      against the diff; packet not accepted until then — see 00-overview.md).
