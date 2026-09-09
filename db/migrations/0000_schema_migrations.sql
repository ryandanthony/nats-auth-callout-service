-- 0000_schema_migrations.sql
-- Tracks which migrations have run (P12's tiny migration runner, no heavyweight framework).
-- Always applied first by the runner regardless of ordering, since every other migration's
-- idempotent-application check depends on it existing.

CREATE TABLE IF NOT EXISTS schema_migrations (
  filename     text PRIMARY KEY,
  applied_at   timestamptz NOT NULL DEFAULT now()
);
