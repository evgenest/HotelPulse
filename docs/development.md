# Development Guide

## Project Goal

Build a practical mini-project covering as many relevant hospitality-platform technologies as possible in **1-2 days**. The scope is deliberately minimal — enough to demonstrate real-world patterns without over-engineering.

The project was designed around the hotel channel-management domain and demonstrates the core architecture patterns expected in this kind of product: event-driven systems, data-driven architecture, SaaS platform development.

---

## Day 1 — Backend + Local Infrastructure (~6-8 h)

### Step 1 — Repo skeleton (15 min)
```bash
mkdir hotelpulse && cd hotelpulse
git init
# Create directory structure
mkdir -p apps/api apps/worker apps/web k8s
```

### Step 2 — MongoDB + RabbitMQ via docker-compose (30 min)

Start just the infrastructure services:
```bash
docker compose up -d mongo rabbitmq
```

Verify RabbitMQ Management UI opens at `http://localhost:15672` (guest / guest).
You should see the default vhost and no queues yet.

### Step 3 — .NET 10 API (2 h)

Key decisions made in `apps/api/Program.cs`:
- **Minimal API** style (no controllers) — matches .NET 10 best practices
- **MongoDB.Driver** for database access
- **RabbitMQ.Client** (raw, no MassTransit) — learn the primitive first
- Hotels are **seeded on first startup** so no manual DB setup is needed
- `POST /api/bookings` returns **202 Accepted** immediately — never blocks on the worker

Packages used:
```xml
<PackageReference Include="MongoDB.Driver" Version="2.28.0" />
<PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
```

### Step 4 — .NET 10 Worker Service (1.5 h)

Key decisions in `apps/worker/BookingConsumer.cs`:
- Extends **BackgroundService** (ASP.NET Core's hosted service pattern)
- Reconnects to RabbitMQ automatically on failure (retry loop in `ExecuteAsync`)
- Sets `BasicQos(prefetchCount: 1)` — processes one message at a time, safe for demos
- **Does not use auto-ack** — message is only ack'd after successful Mongo update
- Simulates a 3-step pipeline with real delays (700ms + 800ms + 900ms)

### Step 5 — Dockerfiles + docker-compose (30 min)

Both .NET projects use multi-stage builds:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

The `aspnet` runtime image is much smaller than `sdk` — important for production images.

### Step 6 — Smoke test (30 min)

```bash
docker compose up -d

# Create a booking
curl -s -X POST http://localhost:8080/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"hotelId":"h_alpina","roomId":"r1","guestName":"Test User","checkIn":"2026-07-01","checkOut":"2026-07-03","nights":2,"total":640}'

# Note the returned id, wait 3 seconds, then:
curl http://localhost:8080/api/bookings/<id>
# status should now be "confirmed" or "rejected"
```

Watch the RabbitMQ UI at `localhost:15672` → Queues → `bookings.created` to see message flow.

---

## Day 2 — Frontend + Kubernetes (~6-8 h)

### Step 7 — Nuxt 4 frontend (3 h)

Key decisions in `apps/web/`:
- **`ssr: false`** in `nuxt.config.ts` — SPA mode, avoids SSR/CSR URL-split complexity
- Custom CSS (no Tailwind) — ported directly from the Claude Design prototype
- `useQueueStore.ts` — module-level reactive singleton for queue visualizer state
- `useBookingStore.ts` — booking history in `localStorage` (no backend needed)
- Polling via `setInterval` in `onMounted`, cleared in `onUnmounted`
- `definePageMeta({ layout: false })` in each page — each page manages its own shell

The three pages:
| Page | Route | Key behaviour |
|------|-------|---------------|
| Hotel list | `/` | Fetches hotels, renders grid |
| Hotel detail | `/hotels/[id]` | Room picker, booking form modal |
| Booking status | `/bookings/[id]` | Polls API every 1.5s until terminal |

### Step 8 — Full docker-compose up (1 h)

```bash
docker compose up --build -d
docker compose logs -f   # watch all 5 services start up
```

Check the `depends_on` + health checks in `docker-compose.yml` — the API and Worker only start after Mongo and RabbitMQ are healthy.

### Step 9 — Kubernetes (2-3 h)

#### Prerequisites
- `kind` installed: `winget install Kubernetes.kind` (or `brew install kind` on Mac)
- `kubectl` installed and configured

#### Manifest overview

| File | Resource | Notes |
|------|----------|-------|
| `namespace.yaml` | Namespace `hotelpulse` | Isolates all resources |
| `mongo.yaml` | StatefulSet + Service | Persistent volume for data |
| `rabbitmq.yaml` | Deployment + Service | Readiness probe checks AMQP port |
| `api.yaml` | Deployment + Service | HTTP readiness probe at `/health` |
| `worker.yaml` | Deployment | No service — consumes from queue only |
| `web.yaml` | Deployment + Service | Serves the Nuxt SPA |
| `ingress.yaml` | Ingress | Routes `/api/*` to api, `/` to web |

#### Deploy commands
```bash
# Build and load images
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

kind create cluster --name hotelpulse
kind load docker-image hotelpulse-api:dev    --name hotelpulse
kind load docker-image hotelpulse-worker:dev --name hotelpulse
kind load docker-image hotelpulse-web:dev    --name hotelpulse

kubectl apply -f k8s/

# Check status
kubectl get pods -n hotelpulse
kubectl get services -n hotelpulse
```

#### Access the app
```bash
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
```

### Step 10 — README (30 min)

Already done. See the root `README.md` for all three launch scenarios.

---

## Verification Checklist

### Level 1 — API only
```bash
curl http://localhost:8080/api/hotels | jq length        # → 6
curl http://localhost:8080/health                         # → {"status":"ok"}
```

### Level 2 — Full async flow (docker-compose)
```bash
# 1. Create booking
ID=$(curl -s -X POST http://localhost:8080/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"hotelId":"h_porter","roomId":"r2","guestName":"Test","checkIn":"2026-07-01","checkOut":"2026-07-03","nights":2,"total":390}' \
  | jq -r '.id')
echo "Created: $ID"

# 2. Check immediately — should be pending
curl -s http://localhost:8080/api/bookings/$ID | jq .status

# 3. Wait 3s and check again — should be confirmed or rejected
sleep 3 && curl -s http://localhost:8080/api/bookings/$ID | jq .status
```

### Level 3 — Frontend e2e
1. Open `http://localhost:3000`
2. Click any hotel → select a room → fill the form → click "Confirm booking"
3. Watch the booking status page — pipeline animation shows each step
4. Status transitions from `pending` → `confirmed` without page reload

### Level 4 — Failure demo
```bash
# Stop the worker
docker compose stop worker

# Create a booking — status will stay "pending" forever
curl -X POST http://localhost:8080/api/bookings ...

# Restart — durable queue replays the message
docker compose start worker
sleep 3
curl http://localhost:8080/api/bookings/<id>   # → confirmed
```

### Level 5 — Kubernetes
```bash
kubectl get pods -n hotelpulse               # all Running
kubectl port-forward svc/web 3000:3000 -n hotelpulse
# Repeat Level 3 e2e test
```

---

## Intentional Scope Limits

| Feature | Why excluded |
|---------|-------------|
| Authentication | 2-day spike — adds significant complexity with no learning benefit |
| Real payments | Out of scope for a demo |
| Unit tests | Spike objective is integration, not coverage |
| CI/CD | GitHub Actions is a good next step, not required for day 1-2 |
| Pinia | Composables with module-level state are sufficient for this scope |
| MassTransit | Raw `RabbitMQ.Client` is better for learning — fewer abstractions |

---

## Architecture Notes

- **RabbitMQ**: "The project uses a topic exchange with a durable queue and manual ack. The Worker uses `BasicQos(1)` so it processes one message at a time — safe for a single-instance demo. In production, the next steps would be horizontal Worker scaling and dead-letter queues."

- **Async architecture**: "The API returns 202 immediately without waiting for confirmation. This reflects a data-driven architecture approach. The frontend polls, but SSE or WebSockets would be a natural production upgrade."

- **AI-assisted development**: "Claude helped accelerate the scaffolding. Every file was still reviewed and understood manually — AI handles repetitive boilerplate, while architecture decisions stay explicit."
