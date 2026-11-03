# HotelPulse — notes for AI coding agents

This file tells Claude Code (and any other coding agent) how to work in this
repo. Humans should read `README.md` and `docs/development.md` first.

## What this is

A small hotel booking service. `POST /api/bookings` writes the booking and an
outbox row in one Postgres transaction and returns `202`. A relay publishes to
RabbitMQ, a worker confirms or rejects. Postgres is the source of truth; Redis
only caches reads.

The same contract (`docs/api.md`) has two implementations: .NET (`apps/api`,
`apps/worker`, `apps/HotelPulse.*`) and Laravel (`apps/api-laravel`, see its
own `CLAUDE.md`). They share the SQL schema files and the rules below. A change
to the contract or the rules is made in both, or in neither.

```
apps/HotelPulse.Domain/          booking rules, no I/O — put business logic here
apps/HotelPulse.Infrastructure/  Postgres (Dapper), Redis, RabbitMQ, SQL migrations
apps/api/                        HTTP endpoints (Endpoints.cs) and the outbox relay
apps/worker/                     queue consumer host
apps/api-laravel/                Laravel 13 / PHP 8.5: the same API, relay and worker
apps/web/                        Nuxt 4 / Vue 3 / TypeScript, with a .NET / Laravel switch
tests/HotelPulse.UnitTests/      domain tests, no Docker
tests/HotelPulse.IntegrationTests/  real Postgres, Redis, RabbitMQ
docs/specs/                      feature specs — one per feature
docs/adr/                        architecture decision records — read before changing concurrency, outbox or caching
docs/why-not-mvp.md              every production decision on one page
scripts/load/                    k6 load test; results in docs/benchmarks.md
```

## Commands

```bash
npm run test:unit         # fast, no Docker
npm run test:integration  # needs Docker (Testcontainers) or the *_URI env vars
npm test                  # both
npm run typecheck         # frontend
# Laravel, in apps/api-laravel (PHP 8.5):
./vendor/bin/pest && ./vendor/bin/pint --test && ./vendor/bin/phpstan analyse
docker compose up -d --build
```

Use `HOTELPULSE_STEP_DELAY_MS=0` and `HOTELPULSE_CONFIRM_PROBABILITY=1` for
deterministic integration runs. CI (`.github/workflows/ci.yml`) runs the same
commands; a change is not done until CI is green.

## How features are built here: spec first

1. Write or update a spec in `docs/specs/<feature>.md`: problem, rules, API
   table (case → status code), data changes, cache effects, tests.
   `docs/specs/booking-cancellation.md` is the reference example.
2. Get the spec reviewed before writing code.
3. Implement in this order: domain rule + unit tests → migration →
   repository → endpoint → integration tests → frontend → docs.
4. Every row of the spec's API table has an integration test.
5. Update `docs/api.md` **and** `docs/api.ru.md`. Docs ship in both languages.

## Rules that must not be broken

- **Never edit an applied migration.** Add `NNN_name.sql` in
  `apps/HotelPulse.Infrastructure/Migrations/`. Migrations run in name order
  inside a transaction at API start.
- **Concurrency is enforced in SQL, not in application code (C# or PHP).** Status changes are
  compare-and-set (`WHERE status = ...`). Double booking is prevented by the
  `bookings_no_overlapping_stay` exclusion constraint. Do not replace these
  with read-then-write checks in application code.
- **Only active statuses hold a room**: `pending` and `confirmed`. The
  exclusion constraint, `bookings_availability_idx` and `AvailabilitySql`
  all filter on that set; keep them in sync.
- **Every write that changes availability invalidates the hotel's cache**:
  `RedisHotelCache.InvalidateHotelAsync(hotelId)` in .NET,
  `HotelCache::invalidateHotel($hotelId)` in Laravel.
- **Redis failures must not fail a request.** The cache returns `BYPASS` and
  the API reads Postgres.
- Parallel inserts for one room take `pg_advisory_xact_lock` on the room
  first (ADR-0005), and a write that recomputes a column must compute it in
  the UPDATE from the stored value, not from an earlier read (ADR-0003).
- The worker must stay idempotent: a redelivered message for a finished
  booking is acked and ignored.
- No secrets in the repo. Connection strings come from env vars
  (`POSTGRES_URI`/`DATABASE_URL`, `REDIS_URI`/`REDIS_URL`, `RABBITMQ_URI`/`AMQP_URL`).

## Conventions

- C#: records for data, static classes for pure rules, async all the way,
  `CancellationToken` on every I/O method.
- PHP: see `apps/api-laravel/CLAUDE.md` — `declare(strict_types=1)`, final
  readonly classes for data, PHPStan level 8 clean, no `$model->save()` after a
  read (ADR-0015).
- SQL: snake_case, explicit column lists, half-open date ranges `[check_in, check_out)`.
- Tests: names describe behaviour (`A_second_cancel_succeeds_and_changes_nothing`).
- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`), one
  logical change per commit.
- Version: `Directory.Build.props`, `apps/web/package.json` and
  `apps/api-laravel/config/hotelpulse.php` change together (a test checks it).
