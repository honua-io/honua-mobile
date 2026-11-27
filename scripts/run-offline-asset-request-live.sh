#!/usr/bin/env bash
set -euo pipefail
umask 077
mobile_root=$(cd -- "$(dirname -- "$0")/.." && pwd)
: "${HONUA_ASSET_E2E_SERVER_CHECKOUT:?Set to the Server source checkout matching the published binary}"
: "${HONUA_ASSET_E2E_SERVER_PUBLISH:?Set to the directory containing the published Honua.Server executable}"
: "${HONUA_ASSET_E2E_FIELD_CHECKOUT:?Set to the Field checkout containing scripts/e2e/fixtures}"
: "${HONUA_ASSET_E2E_NUGET_CONFIG:?Set to the consumer NuGet config for the typed WorkTask SDK}"
: "${HONUA_ASSET_E2E_TASK_SDK_VERSION:?Set to the verified version of the typed WorkTask SDK}"
RUNNER_TEMP=$(mkdir -p "${HONUA_ASSET_E2E_REPORT_DIR:-$mobile_root/TestResults/asset-request}" &&
    cd "${HONUA_ASSET_E2E_REPORT_DIR:-$mobile_root/TestResults/asset-request}" && pwd)
base_url="https://127.0.0.1:${HONUA_ASSET_E2E_PORT:-5196}"
git -C "$HONUA_ASSET_E2E_SERVER_CHECKOUT" rev-parse HEAD > "$RUNNER_TEMP/server-source.txt"
git -C "$HONUA_ASSET_E2E_FIELD_CHECKOUT" rev-parse HEAD > "$RUNNER_TEMP/field-source.txt"
git -C "$mobile_root" rev-parse HEAD > "$RUNNER_TEMP/mobile-source.txt"
state=$(mktemp -d "$RUNNER_TEMP/secrets.XXXXXX")
container=honua-asset-request-e2e-${state##*.}
server_pid=
cleanup() {
  if [[ -n $server_pid ]]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi
  docker rm -fv "$container" >/dev/null 2>&1 || true
  rm -r -- "$state"
}
trap cleanup EXIT
cd "$HONUA_ASSET_E2E_FIELD_CHECKOUT"
POSTGIS_IMAGE=${HONUA_ASSET_E2E_POSTGIS_IMAGE:-postgis/postgis:18-3.6}
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
    curl --silent --fail --cacert "$state/tls.crt" "$base_url/healthz/ready" >/dev/null
}
start_server() {
    (
    cd "$HONUA_ASSET_E2E_SERVER_PUBLISH"
    ASPNETCORE_ENVIRONMENT=Development \
    Kestrel__Endpoints__Https__Url="$base_url" \
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
    Oidc__AdminAuthCallbackUri="$base_url/admin/auth/callback" \
    Oidc__TokenValidation__SymmetricSigningKey="$(cat "$state/signing")" \
    Oidc__TokenValidation__ValidIssuers__0=https://fixture.invalid \
    Oidc__TokenValidation__ValidAudiences__0=honua-fixture \
    Licensing__DevGrantEdition=Pro Geocoding__Enabled=false \
        exec ./Honua.Server
    ) > "$state/server.log" 2>&1 &
    server_pid=$!
    wait_for server_ready
}
printf "%s\n" initial-server >> "$RUNNER_TEMP/assigned-work-stage.txt"
start_server
db=(docker exec -i "$container" psql -X -v ON_ERROR_STOP=1 -U postgres -d honua)
"${db[@]}" < scripts/e2e/fixtures/field-runtime-inspection.sql >/dev/null
sed -n '/^CREATE OR REPLACE FUNCTION honua\.seed_metadata_v2_compat_snapshot()/,/^\$\$;/p' \
    "$HONUA_ASSET_E2E_SERVER_CHECKOUT/tests/seed/base-schema.sql" | "${db[@]}" >/dev/null
"${db[@]}" -1 -f - < scripts/e2e/fixtures/activate-field-runtime-metadata.sql >/dev/null
printf "%s\n" restarting-with-seed >> "$RUNNER_TEMP/assigned-work-stage.txt"
kill "$server_pid"
wait "$server_pid" || true
server_pid=
start_server
printf "%s\n" importing-assigned-work-form >> "$RUNNER_TEMP/assigned-work-stage.txt"
printf 'X-API-Key: %s\n' "$(cat "$state/admin")" > "$state/admin-header"
python3 scripts/e2e/fixtures/write-field-runtime-workbook.py --assigned-work "$state/inspection.xlsx"
api=(curl --fail --silent --show-error --cacert "$state/tls.crt" -H @"$state/admin-header")
base="$base_url/api/v1/admin/forms"
"${api[@]}" -F "file=@$state/inspection.xlsx" "$base/import?serviceId=field_inspections&layerId=68950" > "$state/draft.json"
version=$(jq -er '.version | select(type == "number" and . > 0)' "$state/draft.json")
printf "%s\n" publishing-assigned-work-form >> "$RUNNER_TEMP/assigned-work-stage.txt"
"${api[@]}" -X POST "$base/field_runtime_inspection/versions/$version/publish" > /dev/null
printf "%s\n" seeding-asset-profile >> "$RUNNER_TEMP/assigned-work-stage.txt"
python3 - "$state" "$base_url" <<'PYTHON'
import base64
import hashlib
import hmac
import json
from pathlib import Path
import ssl
import sys
import time
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def segment(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=")


def main(directory, username, token_name):
    base = sys.argv[2]
    now = int(time.time())
    header = segment(b'{"alg":"HS256","typ":"JWT"}')
    claims = segment(json.dumps({
        "iss": "https://fixture.invalid", "aud": "honua-fixture", "sub": username,
        "roles": ["editor"], "nbf": now - 10, "exp": now + 1800,
    }).encode())
    signature = segment(hmac.digest((directory / "signing").read_bytes().strip(),
                                    header + b"." + claims, hashlib.sha256))
    jwt = b".".join((header, claims, signature)).decode()
    context = ssl.create_default_context(cafile=str(directory / "tls.crt"))

    def post(route, data, headers):
        with urlopen(Request(base + route, data, headers), context=context, timeout=20) as response:
            return json.load(response)

    token = post("/sharing/rest/generateToken", urlencode({
        "username": username, "password": jwt, "client": "requestip", "f": "json",
    }).encode(), {"Content-Type": "application/x-www-form-urlencoded"})["token"]
    (directory / token_name).write_text(token)
main(Path(sys.argv[1]), "asset-request-crew", "a.token")
PYTHON
service=svc-field-inspections-feature
asset=$("${db[@]}" -Atc "INSERT INTO public.features (layer_id,geometry,attributes) VALUES (68950, ST_SetSRID(ST_MakePoint(-122.4,47.2),4326), '{\"name\":\"Seeded pole\",\"total\":1}') RETURNING objectid;" | head -1)
uuid() { cat /proc/sys/kernel/random/uuid; }
jq -n --arg cmd "$(uuid)" --arg service "$service" '{commandId:$cmd,serviceId:$service,layerId:68950,name:"Asset request fixture",fields:{assetId:"objectid"},conditionLabels:["Very poor","Poor","Fair","Good","Very good"],inspectionFormId:"field_runtime_inspection",serviceFormId:"field_runtime_inspection",state:"active"}' >"$state/profile.json"
"${api[@]}" -H 'Content-Type: application/json' --data-binary @"$state/profile.json" "$base_url/api/v1/assets/profiles" >"$state/profile-result.json"
profile=$(jq -er '.profile.profileId' "$state/profile-result.json")
task=$(uuid)
command=$(uuid)
jq -n --arg server "$base_url" --arg d "$state" --arg task "$task" --arg command "$command" --arg profile "$profile" --arg asset "$asset" --arg service "$service" '{server:$server,serviceId:$service,subject:"asset-request-crew",profileId:$profile,assetId:$asset,taskId:$task,commandId:$command,crewTokenFile:($d+"/a.token")}' >"$state/test-config.json"
export SSL_CERT_FILE="$state/tls.crt" HONUA_MOBILE_ASSET_REQUEST_LIVE_CONFIG="$state/test-config.json"
dotnet test "$mobile_root/tests/Honua.Mobile.Offline.Tasks.Tests" -p:RestoreConfigFile="$HONUA_ASSET_E2E_NUGET_CONFIG" -p:HonuaWorkTasksSdkVersion="$HONUA_ASSET_E2E_TASK_SDK_VERSION" --filter FullyQualifiedName~OfflineAssetRequestLiveTests --logger 'trx;LogFileName=asset-request.trx' --results-directory "$RUNNER_TEMP" >"$RUNNER_TEMP/test.log" 2>&1
"${db[@]}" -At > "$RUNNER_TEMP/readback.json" <<SQL
SELECT json_build_object(
    'tasks', (SELECT count(*) FROM honua.work_tasks),
    'receipts', (SELECT count(*) FROM honua.asset_command_receipts),
    'matchingCreateReceipts', (SELECT count(*) FROM honua.asset_command_receipts
        WHERE subject_id='$task' AND subject_kind='work_task' AND command_id='$command' AND action='create'),
    'matchingTask', (SELECT json_build_object(
        'taskId', t.task_id, 'serviceId', t.service_id, 'assetId', t.asset_id, 'title', t.title,
        'status', t.status, 'geometrySource', t.geometry_source,
        'taskGeometry', ST_AsEWKT(t.geometry), 'assetGeometry', ST_AsEWKT(a.geometry)
    ) FROM honua.work_tasks t
    JOIN public.features a ON a.layer_id=68950 AND a.objectid::text=t.asset_id
    WHERE t.task_id='$task' AND t.service_id='$service' AND t.task_type='request'
        AND t.status='unassigned' AND t.assignee IS NULL AND t.version=1
        AND t.profile_id='$profile' AND t.asset_id='$asset'
        AND t.title='Inspect asset from offline queue' AND t.geometry_source='asset'
        AND ST_SRID(t.geometry)=4326 AND ST_SRID(a.geometry)=4326 AND ST_Equals(t.geometry, a.geometry))
);
SQL
jq -e '.tasks == 1 and .receipts == 2 and .matchingCreateReceipts == 1 and .matchingTask != null' \
    "$RUNNER_TEMP/readback.json" >/dev/null
