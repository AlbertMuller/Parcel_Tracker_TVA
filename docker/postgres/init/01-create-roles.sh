#!/bin/sh
# Runs once, when the Postgres data volume is first initialised (docker-entrypoint-initdb.d).
# Creates two login roles with least privilege:
#   - migrator: owns the database and schema; used only by the one-shot "migrate" service.
#   - app:      used by the API; may read, insert and update rows but cannot change the schema or delete data.
set -eu

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v migrator="$MIGRATOR_DB_USER" -v migrator_password="$MIGRATOR_DB_PASSWORD" \
  -v app="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" <<'EOSQL'
CREATE ROLE :"migrator" LOGIN PASSWORD :'migrator_password';
CREATE ROLE :"app" LOGIN PASSWORD :'app_password';

-- The migrator owns the database, so (on PG 15+) it also owns the public schema via pg_database_owner.
ALTER DATABASE :"db" OWNER TO :"migrator";
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO :"app";
GRANT USAGE ON SCHEMA public TO :"app";

-- Tables and sequences the migrator creates later are automatically usable by the app role.
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE ON TABLES TO :"app";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"app";
EOSQL

echo "Created roles '$MIGRATOR_DB_USER' (schema owner) and '$APP_DB_USER' (read/insert/update)."
