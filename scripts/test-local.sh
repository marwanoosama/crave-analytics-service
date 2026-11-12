#!/usr/bin/env bash
# Local end-to-end smoke test for analytics-service (plan.md acceptance runs).
# Assumes Mongo (27017) and RabbitMQ (5672) are running, .env is filled.
# Starts a fake core-service double + the app, runs the documented scenarios,
# prints PASS/FAIL per check, and cleans up. Does NOT publish events — use
# publish-test-event.sh for the consumer path.
set -euo pipefail
cd "$(dirname "$0")/.."

BASE_URL="${1:-http://localhost:5000}"
PORT="${PORT:-5000}"
CORE_PORT=3000
PASS=0; FAIL=0
PIDS=()
cleanup() { for pid in "${PIDS[@]:-}"; do kill "$pid" 2>/dev/null || true; done; }
trap cleanup EXIT

check() { # check <name> <expected> <actual>
    if [[ "$3" == *"$2"* ]]; then PASS=$((PASS+1)); echo "PASS  $1"
    else FAIL=$((FAIL+1)); echo "FAIL  $1  (expected $2, got: $3)"; fi
}

# env from .env first — the fake core reads CORE_INTERNAL_API_KEY
set -a; source .env; set +a
CORE_PORT="${CORE_PORT:-3000}"

# 1. fake core on CORE_PORT
node scripts/fake-core.js > /tmp/opencode/fake-core-test.log 2>&1 & PIDS+=($!)
# 2. app — core pointed at the double
PORT=$PORT CORE_SERVICE_BASE_URL="http://localhost:$CORE_PORT" \
    "$HOME/.dotnet/dotnet" backend/src/Crave.Analytics.Api/bin/Debug/net10.0/Crave.Analytics.Api.dll \
    > /tmp/opencode/test-local-app.log 2>&1 & PIDS+=($!)
# 3. tokens
ACCESS_SECRET="$ACCESS_SECRET" node scripts/mint-dev-tokens.js > /dev/null
source scripts/.dev-tokens.env

for _ in $(seq 1 30); do
    curl -s -o /dev/null "$BASE_URL/api/health" && break
    sleep 1
done

DAYS="/api/analytics/restaurants/42/days?from=2026-01-01&to=2026-01-02"

check "health with mongo up"            '200' "$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/api/health")"
check "no token → 401 envelope"         '"error":"User not authenticated"' "$(curl -s "$BASE_URL$DAYS")"
check "wrong restaurant member → 403"   '"error":"Permission denied"' \
    "$(curl -s -H "Authorization: Bearer $otherOwner7" "$BASE_URL$DAYS")"
check "granted role → 200"              '"success":true' \
    "$(curl -s -H "Authorization: Bearer $owner42" "$BASE_URL$DAYS")"
check "ungranted role (cookie) → 403"   '"error":"Permission denied"' \
    "$(curl -s --cookie "access_token=$staff42" "$BASE_URL$DAYS")"
check "system_admin bypass → 200"       '"success":true' \
    "$(curl -s -H "Authorization: Bearer $admin" "$BASE_URL$DAYS")"
check "missing params → 400"            '"from and to are required (YYYY-MM-DD)"' \
    "$(curl -s -H "Authorization: Bearer $owner42" "$BASE_URL/api/analytics/restaurants/42/days")"
check "inverted range → 400"            '"from must be before or equal to to"' \
    "$(curl -s -H "Authorization: Bearer $owner42" "$BASE_URL/api/analytics/restaurants/42/days?from=2026-01-02&to=2026-01-01")"
check "range too wide → 400"            '"date range must not exceed 366 days"' \
    "$(curl -s -H "Authorization: Bearer $owner42" "$BASE_URL/api/analytics/restaurants/42/days?from=2024-01-01&to=2026-01-02")"
check "unknown restaurant → 200 empty"  '"data":[]' \
    "$(curl -s -H "Authorization: Bearer $admin" "$BASE_URL/api/analytics/restaurants/999/days?from=2026-01-01&to=2026-01-02")"

echo "----"
echo "$PASS passed, $FAIL failed (scenarios only check shapes — publish events with"
echo "scripts/publish-test-event.sh, then re-run with real dates to see live rows)"
exit "$FAIL"
