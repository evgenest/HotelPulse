# Deploy HotelPulse on one VPS

The production file is `docker-compose.prod.yml`. Caddy terminates HTTPS and routes `/api/*` and `/health` to the API and everything else to the web app. Postgres, Redis, and RabbitMQ stay on the internal network.

Nothing in this repository deploys itself. There are no server credentials here. The steps below are what to run on the machine.

## 1. DNS and the host

Point a hostname at the VPS and open TCP 22, 80, and 443. A small machine is enough (2 vCPU, 2–4 GB RAM). Install Docker Engine and the Compose plugin.

```bash
git clone https://github.com/evgenest/HotelPulse.git
cd HotelPulse
cp .env.example .env
```

Edit `.env`:

- `DOMAIN` — the hostname from the DNS record, without `https://`
- `ACME_EMAIL` — used by Caddy for the certificate account
- `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD` — replace `change-me`. Use letters and digits so the AMQP URI stays valid.

## 2. Start

```bash
docker compose -f docker-compose.prod.yml up -d --build
```

Caddy obtains a certificate once port 80 is reachable from the public internet. The site is then `https://$DOMAIN`. The browser calls the API on that same origin.

The API applies SQL migrations on startup, including the hotel catalog. The first booking returns `202` and stays `pending` until the worker consumes it, usually within a few seconds.

## 3. Check

```bash
docker compose -f docker-compose.prod.yml ps
curl -fsS "https://$DOMAIN/health"
curl -fsS "https://$DOMAIN/api/hotels" | head
```

RabbitMQ's management UI is not published. Postgres and Redis are not published either.

## 4. Update

```bash
git pull
docker compose -f docker-compose.prod.yml up -d --build
```

`postgres_data` keeps bookings across recreates. `docker compose -f docker-compose.prod.yml down` keeps that volume. Adding `-v` deletes it.

## 5. What is not included

Accounts, payments, and a hosted URL. After this is running, put the public URL on the live-demo line in `README.md`.

Kubernetes manifests in `k8s/` are a second way to run the same processes. They are not required for the VPS path above. See `docs/kubernetes-storage-en.md` for how the Postgres volume behaves.

## Render free tier

`render.yaml` is a Blueprint for a free demo. It creates two free web services and one free Key Value instance, all in Frankfurt. It does not create Postgres or RabbitMQ.

| Piece | Where it runs | Why |
|---|---|---|
| API | Render web service `hotelpulse-api` | Free web services must bind to `PORT`. Docker image, health check `/health`. |
| Booking worker | Inside the API process | Free Render has no background workers. `RUN_WORKER_IN_PROCESS=true` is set only on this service. `apps/worker` is still the normal process everywhere else. |
| Web | Render web service `hotelpulse-web` | The existing Nuxt Node server. It listens on `0.0.0.0:$PORT`. |
| Redis | Render Key Value `hotelpulse-cache` | Free, internal only (`ipAllowList: []`). `REDIS_URL` is the internal `redis://` URL. |
| Postgres | Neon free | External. May scale to zero. Not a Render database. |
| RabbitMQ | CloudAMQP free | External `amqps://` URL. |

### Neon project

The repo is linked to the Neon project `hotelpulse` (aws-eu-central-1, Postgres 18, branch `production`). `neon.ts` holds the branch policy and `neon deploy` applies it:

```bash
npm install                       # @neon/config and @neon/env for neon.ts
neon init -y --org-id org-late-dawn-97216763 --project-id dawn-dew-90649331 --branch production --services postgres
neon deploy                       # applies neon.ts, writes DATABASE_URL / DATABASE_URL_UNPOOLED to .env.local
```

`.env.local` and `.neon` are gitignored. The API uses `DATABASE_URL`, the **pooled** (`-pooler`) host with `sslmode=require`, over Npgsql TCP. `DATABASE_URL_UNPOOLED` is only for admin work such as `psql` sessions that need session state.

### What you set by hand

On the first Blueprint apply, Render asks for every `sync: false` variable:

1. `DATABASE_URL` on `hotelpulse-api`. Paste the Neon **pooled** connection string. It looks like `postgresql://USER:PASSWORD@ep-….aws.neon.tech/neondb?sslmode=require`. The API keeps that `sslmode=require` (TLS on) and caps the pool at 5 connections. Neon can take a few seconds to wake; the API retries the first connection for about a minute before it gives up and Render restarts the process. Migrations, including the six hotels, run on that first successful connection and are safe to run again.
2. `AMQP_URL` on `hotelpulse-api`. Paste the CloudAMQP URL, including the `amqps://` scheme and the vhost path.

`REDIS_URL` and `NUXT_PUBLIC_API_BASE` are wired by the Blueprint. `CORS_ORIGINS` is the web service's public URL, also wired. `HOTELPULSE_CONFIRM_PROBABILITY` is `1` so a demo booking confirms instead of the usual one-in-ten rejection.

Create the Blueprint from this repo (Dashboard → New → Blueprint, or `render blueprints` against `render.yaml`). Both services build their Dockerfiles. The first request after idle is slow: the free web service sleeps after about 15 minutes, and Neon may be asleep too.

Free Neon databases are paused or removed on Neon's schedule, and the free Render services sleep when nobody is calling them. Neither is a substitute for the VPS compose file above.

## Laravel API on Render

`render.yaml` also creates `hotelpulse-api-laravel`, the Laravel implementation (`apps/api-laravel`). It is one free web service: the image's `all` mode runs Octane, the outbox relay and the worker under supervisord, the same trick as `RUN_WORKER_IN_PROCESS` on the .NET side.

| Variable | Value |
|---|---|
| `APP_KEY` | generated by Render (`generateValue`); the entrypoint adds the `base64:` prefix Laravel expects |
| `DATABASE_URL` | a **separate** Neon branch, for example `laravel` (pooled URL) |
| `DATABASE_URL_UNPOOLED` | the same branch, direct URL; migrations take a session advisory lock |
| `AMQP_URL` | the same CloudAMQP instance; this API uses exchange `hotelpulse.laravel` and queue `bookings.created.laravel` |
| `REDIS_URL` | the shared Key Value; keys are prefixed `laravel:` |

Free services sleep after 15 idle minutes. Running three of them shares the workspace's free instance hours; they only count while awake.

The image refuses to start without `APP_KEY` and retries migrations while a sleeping Neon branch wakes up. Logs are JSON lines on stderr.

Other hosts: run the same image three times with the commands `web`, `relay` and `consume` (for example Fly.io process groups), and run `migrate` as the release step.
