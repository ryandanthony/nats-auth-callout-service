-- 0001_create_auth_applications.sql
-- Application documents as JSONB, reusing the exact file schema v1 shape (doc09 §3, P12).
-- One row per application; the JSONB `spec` is the doc09 application document (schemaVersion +
-- name are still present in the JSON body for consistency with the file source, even though
-- `name` duplicates the primary key column - the same strict deserializer as the files
-- provider (ApplicationFileMapper) reads the JSONB body unmodified, so the two sources can
-- never silently diverge in what "valid" means, ADR 0007).

CREATE TABLE IF NOT EXISTS auth_applications (
  name           text PRIMARY KEY,
  spec           jsonb NOT NULL,
  enabled        boolean NOT NULL DEFAULT true,
  updated_at     timestamptz NOT NULL DEFAULT now()
);

-- NOTIFY 'auth_applications_changed' (PostgresSourceSettings.DefaultNotifyChannel) on any
-- insert/update/delete, a latency optimization only - the provider's poll loop is always the
-- load-bearing change-detection mechanism (ADR 0017: the poll interval also drives the
-- LastSuccessfulSync staleness heartbeat), so a client that never LISTENs still converges
-- within one poll interval.
CREATE OR REPLACE FUNCTION notify_auth_applications_changed() RETURNS trigger AS $$
BEGIN
  PERFORM pg_notify('auth_applications_changed', '');
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS auth_applications_notify ON auth_applications;
CREATE TRIGGER auth_applications_notify
  AFTER INSERT OR UPDATE OR DELETE ON auth_applications
  FOR EACH STATEMENT
  EXECUTE FUNCTION notify_auth_applications_changed();
