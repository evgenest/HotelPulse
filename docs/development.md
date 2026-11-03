# Development

## Layout

```
apps/HotelPulse.Domain/           booking rules (no I/O)
apps/HotelPulse.Infrastructure/  PostgreSQL, Redis, RabbitMQ, SQL migrations
apps/api/                        HTTP API and the outbox relay
apps/worker/                     queue consumer
apps/web/                        Nuxt app
tests/HotelPulse.UnitTests/
tests/HotelPulse.IntegrationTests/
```

`POST /api/bookings` validates the stay, inserts the booking and an outbox row in one transaction, and returns `202`. A relay publishes `booking.created` to the durable queue `bookings.created`. The worker is idempotent: a second delivery of a booking that is already confirmed or rejected is acknowledged and ignored. Overlapping active stays are rejected by the exclusion constraint (`409`).

## Run the tests

Unit tests do not need Docker:

```bash
npm run test:unit
```

Integration tests talk to real Postgres, Redis, and RabbitMQ. With Docker running and no connection strings set, Testcontainers starts them. In CI the same tests use the service containers from `.github/workflows/ci.yml` when `POSTGRES_URI`, `REDIS_URI`, and `RABBITMQ_URI` are set.

```bash
npm run test:integration
npm test                 # both projects
npm run typecheck        # Nuxt / Vue
```

`HOTELPULSE_STEP_DELAY_MS=0` and `HOTELPULSE_CONFIRM_PROBABILITY=1` make the pipeline immediate and deterministic. Leave them unset for the product pacing (700 ms, 800 ms, 900 ms) and the 90% confirm rate.

`DATABASE_URL`, `REDIS_URL`, and `AMQP_URL` override `POSTGRES_URI`, `REDIS_URI`, and `RABBITMQ_URI`. `RUN_WORKER_IN_PROCESS=true` starts the consumer inside the API process; leave it unset so `apps/worker` stays the consumer. `PORT` is the HTTP port when a host such as Render sets it.

## Local processes without Compose

Postgres, Redis, and RabbitMQ have to be reachable at the defaults in `HotelPulseSettings` (`localhost`, database `hotelpulse` / `hotelpulse`, guest RabbitMQ on localhost). Then:

```bash
dotnet run --project apps/api
dotnet run --project apps/worker
cd apps/web && pnpm install && pnpm dev
```

The API migrates on startup. Start it before the worker.

## SQL

Index choice and the measured `EXPLAIN` plans are in [`docs/sql.md`](sql.md).

## Frontend

`apps/web` is a Nuxt 4 SPA. `pnpm typecheck` (or `pnpm test`) runs `nuxt typecheck`. The booking form sends nights and total; the API recomputes them from the room price and rejects a mismatch.
