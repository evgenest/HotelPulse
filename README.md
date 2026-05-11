# HotelPulse

A minimal hotel-booking SaaS demo that covers the full DIRS21 tech stack in a single project: **.NET 8 · Vue/Nuxt 3 · RabbitMQ · MongoDB · Docker · Kubernetes**.

The core idea: booking confirmations are **async**. A POST to `/api/bookings` immediately returns `202 Accepted` with `status: pending`, publishes a message to RabbitMQ, and a separate Worker Service processes it — then the frontend polls until status flips to `confirmed` or `rejected`.

---

## Architecture

```
[Nuxt 3 SPA]  ── HTTP ──►  [.NET 8 Web API]  ──► [MongoDB]
                                    │
                                    └── publish ──► [RabbitMQ]  ──► [.NET Worker]
                                                                          │
                                                                          ▼
                                                                    [MongoDB update]
```

### Services (5 containers)

| Container  | Technology                   | Exposes          |
|------------|------------------------------|------------------|
| `web`      | Nuxt 3 (Node 20)             | `:3000`          |
| `api`      | ASP.NET Core 8 Minimal API   | `:8080`          |
| `worker`   | .NET 8 Worker Service        | —                |
| `mongo`    | MongoDB 7                    | `:27017`         |
| `rabbitmq` | RabbitMQ 3 + Management UI   | `:5672` `:15672` |

---

## Tech Stack

| Technology       | Where used                                                        |
|------------------|-------------------------------------------------------------------|
| C# / .NET 8      | `apps/api` (Minimal API) + `apps/worker` (BackgroundService)     |
| REST API         | `/api/hotels`, `/api/bookings`                                    |
| RabbitMQ         | `bookings.created` queue between API and Worker                   |
| Vue 3 / Nuxt 3   | Frontend SPA                                                      |
| TypeScript       | All frontend code                                                 |
| MongoDB          | Collections: `hotels`, `bookings`                                 |
| Docker           | Dockerfile per service + `docker-compose.yml`                     |
| Kubernetes       | 7 manifests in `k8s/` — use `kind` or Docker Desktop K8s         |
| AI-assisted dev  | Built end-to-end with Claude — worth mentioning at the interview  |

---

## Repository Structure

```
hotelpulse/
├── docker-compose.yml
├── README.md
├── README.ru.md
├── docs/
│   ├── api.md                # API contract + RabbitMQ message format
│   ├── api.ru.md
│   ├── development.md        # Day-by-day guide + verification steps
│   └── development.ru.md
├── apps/
│   ├── api/                  # ASP.NET Core 8 Minimal API
│   │   ├── HotelPulse.Api.csproj
│   │   ├── Program.cs        # all endpoints + DI + seed data
│   │   ├── Models/
│   │   ├── Messaging/        # BookingPublisher.cs
│   │   └── Dockerfile
│   ├── worker/               # .NET 8 Worker Service
│   │   ├── HotelPulse.Worker.csproj
│   │   ├── Program.cs
│   │   ├── BookingConsumer.cs
│   │   └── Dockerfile
│   └── web/                  # Nuxt 3 frontend
│       ├── nuxt.config.ts
│       ├── app.vue
│       ├── assets/css/main.css
│       ├── composables/      # useApi, useQueueStore, useBookingStore
│       ├── components/       # 11 Vue components
│       ├── pages/
│       │   ├── index.vue           # hotel list
│       │   ├── hotels/[id].vue     # hotel detail + booking form
│       │   └── bookings/[id].vue   # booking status (polling)
│       └── Dockerfile
└── k8s/
    ├── namespace.yaml
    ├── mongo.yaml            # StatefulSet + Service
    ├── rabbitmq.yaml         # Deployment + Service
    ├── api.yaml              # Deployment + Service
    ├── worker.yaml           # Deployment
    ├── web.yaml              # Deployment + Service
    └── ingress.yaml          # nginx Ingress
```

---

## Quick Start

### Option 1 — Docker Compose (recommended for first run)

```bash
docker compose up -d

# verify
curl http://localhost:8080/api/hotels          # list of 6 hotels
curl http://localhost:8080/health              # {"status":"ok"}
open http://localhost:3000                     # frontend
open http://localhost:15672                    # RabbitMQ UI (guest / guest)
```

### Option 2 — Local (no Docker)

Prerequisites: .NET 8 SDK, Node 20, MongoDB running on 27017, RabbitMQ on 5672.

```bash
# Terminal 1 — API
cd apps/api
dotnet run

# Terminal 2 — Worker
cd apps/worker
dotnet run

# Terminal 3 — Frontend
cd apps/web
npm install
npm run dev
```

### Option 3 — Kubernetes (kind)

```bash
# 1. Build images
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

# 2. Create cluster and load images
kind create cluster --name hotelpulse
kind load docker-image hotelpulse-api:dev    --name hotelpulse
kind load docker-image hotelpulse-worker:dev --name hotelpulse
kind load docker-image hotelpulse-web:dev    --name hotelpulse

# 3. Deploy
kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # wait until all Running

# 4. Access
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

---

## Failure Demo (great for interviews)

```bash
# Stop the worker — bookings will stay "pending"
docker compose stop worker

# Create a booking in the UI — notice it never confirms
# Restart the worker — RabbitMQ's durable queue replays the message
docker compose start worker
```

This demonstrates the real value of a message queue: **durability under partial failure**.

---

## Intentional Scope Limits

The following were deliberately excluded to keep the project completable in 1-2 days:

- Authentication / authorization
- Real payments
- Unit / integration tests
- CI/CD pipeline
- Pinia or complex state management (composables are sufficient)

See [`docs/development.md`](docs/development.md) for the full day-by-day guide.
See [`docs/api.md`](docs/api.md) for the API contract and queue message format.
