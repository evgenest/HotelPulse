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
