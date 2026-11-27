-- Read-only access for Power BI or another BI tool to the reporting views only.
-- Run as the database owner after the migrations. Create the login separately with a password from your secret store:
--   CREATE ROLE powerbi_reader LOGIN PASSWORD '<from secret store>' IN ROLE ess_reporting;
-- Connect Power BI with that login over TLS (sslmode=require). The views hold no pay, bank, contact or birth-date data.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ess_reporting') THEN
        CREATE ROLE ess_reporting NOLOGIN;
    END IF;
END $$;

REVOKE ALL ON SCHEMA public FROM ess_reporting;
GRANT USAGE ON SCHEMA reporting TO ess_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO ess_reporting;
ALTER DEFAULT PRIVILEGES IN SCHEMA reporting GRANT SELECT ON TABLES TO ess_reporting;
