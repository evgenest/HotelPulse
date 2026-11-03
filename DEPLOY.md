# Deploy HotelPulse on one VPS

The production file is `docker-compose.prod.yml`. Caddy terminates HTTPS and routes `/api/*` and `/health` to the API and everything else to the web app. Postgres, Redis, and RabbitMQ stay on the internal network.

This VPS file and the Kubernetes manifests in `k8s/` run the .NET API. The Laravel API runs locally through the Compose `laravel` profile and on Render as its own service (see below).

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

`render.yaml` is a Blueprint for a free demo. It creates three free web services (.NET API, Laravel API, web app) and one free Key Value instance, all in Frankfurt. It does not create Postgres or RabbitMQ.

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

`render.yaml` also creates `hotelpulse-api-laravel`, the Laravel implementation (`apps/api-laravel`). It is one free web service: the image's `all` mode runs Octane, the outbox relay and the worker under supervisord, the same trick as `RUN_WORKER_IN_PROCESS` on the .NET side. Measured with a 512 MB limit: about 220 MB idle and 235 MB under load.

It needs its **own database**, not a Neon branch. A branch copies its parent's tables and rows, so the Laravel migrations would collide with the .NET schema and the Laravel API would see .NET bookings.

### Steps

1. **Neon: add a database.** In the `hotelpulse` project, on the `production` branch, open Databases → New database, name it `hotelpulse_laravel`, and use the same owner role as `neondb`.
2. **Neon: copy two connection strings** for that database (Connection details, database `hotelpulse_laravel`):
   - pooled (host contains `-pooler`): this is `DATABASE_URL`;
   - direct (Connection pooling off): this is `DATABASE_URL_UNPOOLED`. Optional; without it migrations run through the pooler, which is safe.
3. **CloudAMQP:** reuse the same instance and copy its `amqps://` URL as `AMQP_URL`. The Laravel API declares its own exchange `hotelpulse.laravel` and queue `bookings.created.laravel`, so it never touches the .NET queue.
4. **Render: sync the Blueprint** (Dashboard → Blueprints → the HotelPulse blueprint → Manual sync, or New → Blueprint on a fresh workspace). Render creates `hotelpulse-api-laravel` and asks for the `sync: false` values above (`DATABASE_URL_UNPOOLED` may stay empty). `APP_KEY` is generated by Render, `REDIS_URL` and `CORS_ORIGINS` are wired from the other services.
5. **Render: redeploy `hotelpulse-web`** after the Laravel service is live, so it picks up `NUXT_PUBLIC_API_BASE_LARAVEL` and shows the .NET / Laravel switch.
6. **Check:**

```bash
curl -fsS https://<laravel-service>.onrender.com/health        # {"status":"ok","version":"1.1.1",...}
curl -fsS https://<laravel-service>.onrender.com/health/live
curl -fsSI https://<laravel-service>.onrender.com/api/hotels | grep -i x-cache
```

Then open the web app, switch to Laravel in the header, make a booking and watch it go to `confirmed`.

### Without a Blueprint (services created by hand)

If the .NET API, the web app and Key Value were created by hand in the Render dashboard, add the Laravel API the same way instead of attaching a Blueprint to running services:

1. Neon and CloudAMQP: steps 1–3 above.
2. Generate an app key locally and keep it out of the repo: `echo "base64:$(openssl rand -base64 32)"`.
3. Render → New → Web Service → this repository, branch `main`:

| Setting | Value |
|---|---|
| Name | `hotelpulse-api-laravel` |
| Region / Instance type | Frankfurt / Free |
| Language | Docker |
| Dockerfile Path | `apps/api-laravel/Dockerfile` |
| Docker Build Context Directory | `apps/api-laravel` |
| Docker Command | `hotelpulse all` |
| Health Check Path | `/health` |

4. Environment of that service: `APP_KEY` (step 2), `DATABASE_URL`, optionally `DATABASE_URL_UNPOOLED`, `AMQP_URL`, `REDIS_URL` (the existing Key Value → Internal Key Value URL), `HOTELPULSE_CONFIRM_PROBABILITY=1`, `OCTANE_WORKERS=2`, `CORS_ORIGINS=https://<web-service>.onrender.com`.
5. Web service → Environment: add `NUXT_PUBLIC_API_BASE_LARAVEL=https://<laravel-service>.onrender.com` and save; Render redeploys it and the switch appears.
6. Check as in step 6 above.

| Variable | Value |
|---|---|
| `APP_KEY` | generated by Render (`generateValue`); the entrypoint adds the `base64:` prefix Laravel expects |
| `DATABASE_URL` | Neon database `hotelpulse_laravel`, pooled URL |
| `DATABASE_URL_UNPOOLED` | optional: the same database, direct URL. Migrations use it when set and the pooled URL otherwise; their lock is transaction-scoped, so it works through the pooler too |
| `AMQP_URL` | the same CloudAMQP instance as the .NET API |
| `REDIS_URL` | the shared Key Value; keys are prefixed `laravel:` |

Free services sleep after 15 idle minutes, and the free plan has 0.1 CPU, so the first request after sleep takes tens of seconds and the API is much slower than in `docs/benchmarks.md`. Three services share the workspace's free instance hours; they only count while awake.

The image refuses to start without `APP_KEY` and retries migrations while a sleeping Neon database wakes up. Logs are JSON lines on stderr.

Other hosts: run the same image three times with the commands `web`, `relay` and `consume` (for example Fly.io process groups), and run `migrate` as the release step.
