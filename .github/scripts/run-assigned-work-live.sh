#!/usr/bin/env bash
set -euo pipefail
umask 077
workspace=$1
field=$2
server=$3
report=$4
mkdir -p "$report"
state=$(mktemp -d "$RUNNER_TEMP/secrets.XXXXXX")
container=honua-combined-e2e-${state##*.}
server_pid=
cleanup() {
  if [[ -n $server_pid ]]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi
  docker rm -fv "$container" >/dev/null 2>&1 || true
  rm -r -- "$state"
}
trap cleanup EXIT
cd "$field"
POSTGIS_IMAGE=postgis/postgis:18-3.6@sha256:4a43e3c3e5d55b9208240fc71db543b79c638766280c625a425bd0a2390989c4
openssl rand -hex 24 > "$state/admin"
openssl rand -hex 32 > "$state/signing"
openssl rand -hex 24 > "$state/pfx-password"
openssl rand -hex 24 > "$state/db-password"
printf 'POSTGRES_DB=honua\nPOSTGRES_PASSWORD=%s\n' "$(cat "$state/db-password")" > "$state/db.env"
docker run --detach --name "$container" --publish 127.0.0.1::5432 \
    --env-file "$state/db.env" "${POSTGIS_IMAGE:?}" >/dev/null
db_port=$(docker port "$container" 5432/tcp)
db_port=${db_port##*:}
wait_for() {
    for ((attempt = 0; attempt < 90; ++attempt)); do
        if "$@"; then return; fi
        sleep 2
    done
    echo "Timed out waiting for $1" >&2
    return 1
}
db_ready() {
    docker exec "$container" pg_isready -h 127.0.0.1 -U postgres -d honua >/dev/null 2>&1
}
wait_for db_ready

# This temporary trust anchor only authenticates the isolated loopback fixture.
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=Honua assigned work test' \
    -addext 'subjectAltName=IP:127.0.0.1' -keyout "$state/tls.key" -out "$state/tls.crt" 2>/dev/null
openssl pkcs12 -export -inkey "$state/tls.key" -in "$state/tls.crt" -out "$state/tls.pfx" \
    -passout "file:$state/pfx-password"
master=$(openssl rand -base64 32)
salt=$(openssl rand -base64 16)
server_ready() {
    curl --silent --fail --cacert "$state/tls.crt" https://127.0.0.1:5197/healthz/ready >/dev/null
}
start_server() {
    (
    cd "$RUNNER_TEMP/published"
    ASPNETCORE_ENVIRONMENT=Development \
    Kestrel__Endpoints__Https__Url=https://127.0.0.1:5197 \
    Kestrel__Endpoints__Https__Certificate__Path="$state/tls.pfx" \
    Kestrel__Endpoints__Https__Certificate__Password="$(cat "$state/pfx-password")" \
    ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=$db_port;Database=honua;Username=postgres;Password=$(cat "$state/db-password")" \
    HONUA_ADMIN_PASSWORD="$(cat "$state/admin")" \
    Security__ConnectionEncryption__MasterKey="$master" \
    Security__ConnectionEncryption__Salt="$salt" \
    Database__MigrationSafety__ContractApplyPolicy=Auto \
    Authentication__PortalToken__Enabled=true \
    Authentication__PortalToken__RequireHttps=true \
    Authentication__PortalCredentialVerifier__UseOidc=true \
    Oidc__Enabled=true Oidc__Generic__Enabled=true \
    Oidc__Generic__Authority=https://fixture.invalid Oidc__Generic__ClientId=honua-fixture \
    Oidc__AdminAuthCallbackUri=https://127.0.0.1:5197/admin/auth/callback \
    Oidc__TokenValidation__SymmetricSigningKey="$(cat "$state/signing")" \
    Oidc__TokenValidation__ValidIssuers__0=https://fixture.invalid \
    Oidc__TokenValidation__ValidAudiences__0=honua-fixture \
    Licensing__DevGrantEdition=Pro Geocoding__Enabled=false \
        exec ./Honua.Server
    ) > "$state/server.log" 2>&1 &
    server_pid=$!
    wait_for server_ready
}
printf "%s\n" initial-server >> "$report/stages.txt"
start_server
db=(docker exec -i "$container" psql -X -v ON_ERROR_STOP=1 -U postgres -d honua)
"${db[@]}" < scripts/e2e/fixtures/field-runtime-inspection.sql >/dev/null
sed -n '/^CREATE OR REPLACE FUNCTION honua\.seed_metadata_v2_compat_snapshot()/,/^\$\$;/p' \
    "$server/tests/seed/base-schema.sql" | "${db[@]}" >/dev/null
"${db[@]}" -1 -f - < scripts/e2e/fixtures/activate-field-runtime-metadata.sql >/dev/null
printf "%s\n" restarting-with-seed >> "$report/stages.txt"
kill "$server_pid"
wait "$server_pid" || true
server_pid=
start_server
printf "%s\n" importing-assigned-work-form >> "$report/stages.txt"
printf 'X-API-Key: %s\n' "$(cat "$state/admin")" > "$state/admin-header"
python3 scripts/e2e/fixtures/write-field-runtime-workbook.py --assigned-work "$state/inspection.xlsx"
api=(curl --fail --silent --show-error --cacert "$state/tls.crt" -H @"$state/admin-header")
base=https://127.0.0.1:5197/api/v1/admin/forms
"${api[@]}" -F "file=@$state/inspection.xlsx" "$base/import?serviceId=field_inspections&layerId=68950" > "$state/draft.json"
version=$(jq -er '.version | select(type == "number" and . > 0)' "$state/draft.json")
printf "%s\n" publishing-assigned-work-form >> "$report/stages.txt"
"${api[@]}" -X POST "$base/field_runtime_inspection/versions/$version/publish" > /dev/null
printf "%s\n" issuing-scoped-worker-tokens >> "$report/stages.txt"
python3 "$workspace/.github/scripts/assigned-work-fixture-workers.py" "$state"
jq -n --arg d "$state" '{server:"https://127.0.0.1:5197", serviceId:"field_inspections",formId:"field_runtime_inspection",answers:{name:"Assigned work CI fixture"},plannerKeyFile:($d+"/admin"),crewTokenFile:($d+"/a.token"),alternateCrewTokenFile:($d+"/b.token")}' >"$state/test-config.json"
export SSL_CERT_FILE="$state/tls.crt" HONUA_MOBILE_ASSIGNED_WORK_LIVE_CONFIG="$state/test-config.json"
printf '%s\n' testing-two-scoped-workers >> "$report/stages.txt"
cd "$RUNNER_TEMP"
dotnet test "$workspace/tests/Honua.Mobile.Offline.Tasks.Tests/Honua.Mobile.Offline.Tasks.Tests.csproj" \
    --no-restore -p:HonuaWorkTasksSdkVersion="$HONUA_TASK_SDK_VERSION" \
    --filter FullyQualifiedName~OfflineAssignedWorkLiveTests \
    --logger 'trx;LogFileName=assigned-work.trx' --results-directory "$report"
python3 - "$report/assigned-work.trx" <<'CHECK'
import sys
import xml.etree.ElementTree as ET

counters = ET.parse(sys.argv[1]).find(".//{*}Counters").attrib
if [counters.get(key) for key in ("total", "executed", "passed")] != ["1", "1", "1"]:
    raise SystemExit("Expected exactly one executed and passed two-worker live test.")
CHECK

printf '%s\n' verifying-authoritative-postgis-state >> "$report/stages.txt"
"${db[@]}" -qAt > "$report/postgis-readback.json" <<'SQL'
WITH task_counts AS (
    SELECT t.status, count(*) AS tasks, count(r.service_record_id) AS records,
           count(DISTINCT r.service_record_id) AS unique_records,
           count(*) FILTER (
               WHERE r.service_record_id IS NOT NULL
                 AND (r.actor IS DISTINCT FROM t.assignee OR r.actor IS DISTINCT FROM t.created_by)
           ) AS wrong_owner_records,
           count(*) FILTER (WHERE t.assignee <> t.created_by) AS other_assignee
    FROM honua.work_tasks t LEFT JOIN honua.asset_service_records r USING (task_id)
    GROUP BY t.status
), record_counts AS (
    SELECT count(*) AS records, count(DISTINCT task_id) AS unique_tasks
    FROM honua.asset_service_records
), receipt_counts AS (
    SELECT count(*) AS completion_receipts, count(DISTINCT subject_id) AS unique_completed_subjects
    FROM honua.asset_command_receipts WHERE subject_kind = 'work_task' AND action = 'complete'
)
SELECT jsonb_build_object(
    'tasks', (SELECT jsonb_agg(to_jsonb(task_counts) ORDER BY status) FROM task_counts),
    'records', (SELECT to_jsonb(record_counts) FROM record_counts),
    'receipts', (SELECT to_jsonb(receipt_counts) FROM receipt_counts));
SQL

jq -e '
    .tasks == [
        {status:"cancelled", tasks:1, records:0, unique_records:0, wrong_owner_records:0, other_assignee:0},
        {status:"completed", tasks:3, records:3, unique_records:3, wrong_owner_records:0, other_assignee:0},
        {status:"in_progress", tasks:1, records:0, unique_records:0, wrong_owner_records:0, other_assignee:1},
        {status:"unassigned", tasks:1, records:0, unique_records:0, wrong_owner_records:0, other_assignee:0}
    ]
    and .records == {records:3, unique_tasks:3}
    and .receipts == {completion_receipts:3, unique_completed_subjects:3}
' "$report/postgis-readback.json" >/dev/null
