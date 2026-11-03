SELECT
    format('CREATE ROLE josholson_api LOGIN PASSWORD %L', :'api_password')  AS statement
FROM (SELECT)       AS _
LEFT JOIN pg_roles  AS r  ON r.rolname = 'josholson_api'
WHERE r.rolname IS NULL
\gexec

SELECT
    format('ALTER ROLE josholson_api PASSWORD %L', :'api_password')  AS statement
FROM (SELECT) AS _
\gexec

SELECT
    format('GRANT CONNECT ON DATABASE %I TO josholson_api', current_database())  AS statement
FROM (SELECT) AS _
\gexec

DO $$
DECLARE
  s TEXT;
BEGIN
  FOR s IN
    SELECT
        n.nspname  AS schema_name
    FROM (SELECT)      AS _
    JOIN pg_namespace  AS n  ON n.nspname NOT LIKE 'pg\_%'
                            AND n.nspname NOT IN ('information_schema', 'public')
  LOOP
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO josholson_api', s);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO josholson_api', s);
    EXECUTE format('GRANT USAGE ON ALL SEQUENCES IN SCHEMA %I TO josholson_api', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO josholson_api', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE ON SEQUENCES TO josholson_api', s);
  END LOOP;
END
$$;
