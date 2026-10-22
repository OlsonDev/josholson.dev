SELECT format('CREATE ROLE josholson_api LOGIN PASSWORD %L', :'api_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'josholson_api')
\gexec

SELECT format('ALTER ROLE josholson_api PASSWORD %L', :'api_password')
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO josholson_api', current_database())
\gexec

DO $$
DECLARE
  s TEXT;
BEGIN
  FOR s IN
    SELECT nspname
    FROM pg_namespace
    WHERE nspname NOT LIKE 'pg\_%'
      AND nspname NOT IN ('information_schema', 'public')
  LOOP
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO josholson_api', s);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO josholson_api', s);
    EXECUTE format('GRANT USAGE ON ALL SEQUENCES IN SCHEMA %I TO josholson_api', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO josholson_api', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE ON SEQUENCES TO josholson_api', s);
  END LOOP;
END
$$;
