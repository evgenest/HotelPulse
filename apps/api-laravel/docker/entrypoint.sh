#!/bin/sh
# Starts one HotelPulse process, or all three under supervisord.
set -eu

fail() { echo "{\"level\":\"error\",\"message\":\"$1\"}" >&2; exit 1; }

# Octane boots the encrypter in every worker; without a key the worker dies
# before serving and FrankenPHP only says it "has not reached
# frankenphp_handle_request()". Fail early with the real reason instead.
if [ -z "${APP_KEY:-}" ]; then
  fail "APP_KEY is not set. Generate one with: php artisan key:generate --show"
fi
# Render's generateValue gives raw base64 of 32 random bytes; Laravel wants the base64: prefix.
case "$APP_KEY" in
  base64:*) ;;
  *) if [ "$(printf '%s' "$APP_KEY" | base64 -d 2>/dev/null | wc -c)" -eq 32 ]; then export APP_KEY="base64:$APP_KEY"; fi ;;
esac
[ -n "${DATABASE_URL:-${POSTGRES_URI:-}}" ] || fail "DATABASE_URL is not set."

# Settings come from env vars at start, so caching them here is safe.
php artisan config:cache --no-ansi -q
php artisan route:cache --no-ansi -q

migrate() {
  # Postgres may still be starting (or a free Neon branch waking up): retry.
  for attempt in 1 2 3 4 5 6; do
    if php artisan hotelpulse:migrate --no-ansi; then return 0; fi
    echo "{\"level\":\"warning\",\"message\":\"migration attempt $attempt failed, retrying\"}" >&2
    sleep $((attempt * 2))
  done
  fail "Migrations failed."
}

web() {
  exec php artisan octane:frankenphp --host=0.0.0.0 --port="${PORT}" \
    --workers="${OCTANE_WORKERS:-auto}" --max-requests="${OCTANE_MAX_REQUESTS:-1000}" --log-level=warn
}

case "${1:-web}" in
  web) migrate; web ;;
  relay) exec php artisan hotelpulse:relay --no-ansi ;;
  consume) exec php artisan hotelpulse:consume --no-ansi ;;
  migrate) migrate ;;
  all) migrate; exec supervisord -c /etc/supervisor/hotelpulse.conf ;;
  octane) web ;;
  *) exec "$@" ;;
esac
