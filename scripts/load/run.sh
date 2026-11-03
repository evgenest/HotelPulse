#!/bin/sh
# Runs the load test against one API and then checks the database.
#
#   scripts/load/run.sh <base-url> <postgres-url> [result-name]
#
# Needs Docker (k6 and psql run in containers, on the host network).
# The API must run with HOTELPULSE_BOOKING_RATE_LIMIT=0.
set -eu
BASE_URL=$1
PG_URL=$2
NAME=${3:-run}
HERE=$(cd "$(dirname "$0")" && pwd)
OUT="$HERE/results"
mkdir -p "$OUT"

docker run --rm --network host --user "$(id -u):$(id -g)" -v "$HERE:/load" -e BASE_URL="$BASE_URL" \
  -e SUMMARY_JSON="/load/results/$NAME.json" ${VUS:+-e VUS=$VUS} ${DURATION:+-e DURATION=$DURATION} \
  ${WINDOW_DAYS:+-e WINDOW_DAYS=$WINDOW_DAYS} ${ROOMS:+-e ROOMS=$ROOMS} ${FIRST_DAY:+-e FIRST_DAY=$FIRST_DAY} \
  grafana/k6:2.3.0 run --quiet /load/booking-load.js || K6_FAILED=1

echo "waiting for the worker to drain pending bookings..."
for i in $(seq 1 60); do
  P=$(docker run --rm --network host postgres:18-alpine psql "$PG_URL" -tAc "SELECT count(*) FROM bookings WHERE status = 'pending'")
  [ "$P" = "0" ] && break
  sleep 2
done
docker run --rm --network host -i postgres:18-alpine psql "$PG_URL" -q < "$HERE/after-run.sql"
[ -z "${K6_FAILED:-}" ] || { echo "k6 thresholds failed"; exit 1; }
