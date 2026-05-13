# HotelPulse

A minimal hotel-booking SaaS demo that covers a complete hospitality tech stack in a single project: **.NET 10 · Vue/Nuxt 4 · RabbitMQ · MongoDB · Docker · Kubernetes**.

The core idea: booking confirmations are **async**. A POST to `/api/bookings` immediately returns `202 Accepted` with `status: pending`, publishes a message to RabbitMQ, and a separate Worker Service processes it — then the frontend polls until status flips to `confirmed` or `rejected`.

---

## Architecture

```
[Nuxt 4 SPA]  ── HTTP ──►  [.NET 10 Web API]  ──► [MongoDB]
                                    │
                                    └── publish ──► [RabbitMQ]  ──► [.NET Worker]
                                                                          │
                                                                          ▼
                                                                    [MongoDB update]
```

### Services (5 containers)

| Container  | Technology                   | Exposes          |
|------------|------------------------------|------------------|
| `web`      | Nuxt 4 (Node 24 + pnpm)      | `:3000`          |
| `api`      | ASP.NET Core 10 Minimal API  | `:8080`          |
| `worker`   | .NET 10 Worker Service       | —                |
| `mongo`    | MongoDB 8                    | `:27017`         |
| `rabbitmq` | RabbitMQ 4 + Management UI   | `:5672` `:15672` |

---

## Tech Stack

| Technology       | Where used                                                        |
|------------------|-------------------------------------------------------------------|
| C# / .NET 10     | `apps/api` (Minimal API) + `apps/worker` (BackgroundService)     |
| REST API         | `/api/hotels`, `/api/bookings`                                    |
| RabbitMQ         | `bookings.created` queue between API and Worker                   |
| Vue 3 / Nuxt 4   | Frontend SPA                                                      |
| TypeScript       | All frontend code                                                 |
| MongoDB          | Collections: `hotels`, `bookings`                                 |
| Docker           | Dockerfile per service + `docker-compose.yml`                     |
| Kubernetes       | 7 manifests in `k8s/` — use `kind` or Docker Desktop K8s         |
| AI-assisted dev  | Used to accelerate scaffolding and repetitive implementation work |

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
│   ├── api/                  # ASP.NET Core 10 Minimal API
│   │   ├── HotelPulse.Api.csproj
│   │   ├── Program.cs        # all endpoints + DI + seed data
│   │   ├── Models/
│   │   ├── Messaging/        # BookingPublisher.cs
│   │   └── Dockerfile
│   ├── worker/               # .NET 10 Worker Service
│   │   ├── HotelPulse.Worker.csproj
│   │   ├── Program.cs
│   │   ├── BookingConsumer.cs
│   │   └── Dockerfile
│   └── web/                  # Nuxt 4 frontend
│       ├── nuxt.config.ts
│       ├── app/
│       │   ├── app.vue
│       │   ├── assets/css/main.css
│       │   ├── composables/      # useApi, useQueueStore, useBookingStore
│       │   ├── components/       # 11 Vue components
│       │   └── pages/
│       │       ├── index.vue           # hotel list
│       │       ├── hotels/[id].vue     # hotel detail + booking form
│       │       └── bookings/[id].vue   # booking status (polling)
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

If you are upgrading an existing local Docker volume from MongoDB 7 to MongoDB 8, recreate the volume first:

```bash
docker compose down -v
docker compose up -d
```

The API seeds the `hotels` collection on startup, so the catalog is restored automatically on a fresh volume.

### Option 2 — Local (no Docker)

Prerequisites: .NET 10 SDK, Node 24, pnpm, MongoDB running on 27017, RabbitMQ on 5672.

```bash
# Terminal 1 — API
cd apps/api
dotnet run

# Terminal 2 — Worker
cd apps/worker
dotnet run

# Terminal 3 — Frontend
cd apps/web
pnpm install
pnpm run dev
```

### Option 3 — Kubernetes (`kind` or `minikube`)

#### Option 3A — `kind`

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

# 3. Deploy: namespace first, then the rest
kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # wait until all Running

# 4. Access
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

#### Option 3B — `minikube`

```bash
# 1. Start the cluster
minikube start

# 2. Point Docker CLI to the daemon inside minikube
eval $(minikube docker-env)

# 3. Build images inside minikube
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

# 4. Deploy: namespace first, then the rest
kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # wait until all Running

# 5. Access
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

> If pods get stuck in `ImagePullBackOff` after deploy, the images were most likely built outside the `minikube` Docker daemon. Run `eval $(minikube docker-env)` again and rebuild all three images.

If your cluster already has a PVC with MongoDB 7 data, use a fresh PVC or a fresh namespace before applying `mongo:8`.
A clean PVC is the simplest upgrade path here; the hotel catalog will be reseeded on API startup.

---

## Deployment Guide

### Do I need a VM (like Hetzner) to run this project?

**No** — for development and demos, everything runs locally via Docker Compose (Option 1 above) with no server required.

If you want to publish it on the internet (e.g. for a portfolio link), a single cheap VPS is enough:

| Provider | Cheapest option | Monthly cost |
|----------|----------------|--------------|
| Hetzner | CX22 (2 vCPU, 4 GB RAM) | ~€4 |
| DigitalOcean | Basic Droplet (1 vCPU, 1 GB RAM) | $6 |
| Oracle Cloud | Always Free (2 VMs) | Free |

Just install Docker on the server and run `docker compose up -d`. **Kubernetes is not required** for a simple deployment.

---

### What does Kubernetes actually do in this project?

In a real production company, Kubernetes handles orchestration across many servers. In this demo project, the `k8s/` manifests show *how the same workload would be deployed in a production environment*:

| What | Kubernetes feature used |
|------|-------------------------|
| Restarts the API automatically if it crashes | `Deployment` with restart policy |
| Marks RabbitMQ and API pods ready only when healthy, so Services send traffic to them only then | Readiness probes |
| Gives each service a stable internal hostname | `Service` (ClusterIP) |
| Provides persistent storage for MongoDB | `PersistentVolumeClaim` |
| Optionally routes `/api/*` → API and `/` → Web over HTTP | `Ingress` (requires nginx ingress controller) |
| Isolates all resources from other workloads | `Namespace` `hotelpulse` |

For a single-machine demo, Docker Compose already does all of this. For local Kubernetes quickstarts, use `kubectl port-forward` unless you have also installed an nginx ingress controller and mapped `hotelpulse.local` in your local DNS/hosts file; otherwise `k8s/ingress.yaml` will apply but will not be reachable as documented. The `k8s/` directory shows you know the production path — which is the point at an interview.

---

### Free and cheap Kubernetes options

| Option | Cost | Best for |
|--------|------|----------|
| `kind` on your laptop | **Free** | Local dev / demos (this project) |
| `minikube` on your laptop | **Free** | Local dev / demos |
| `k3s` on a cheap VPS | ~€4/mo (server only) | Lightweight self-hosted K8s |
| Docker Desktop (built-in K8s) | **Free** | Windows / Mac dev machines |
| Oracle Cloud Free Tier | **Free** | Always-free cloud K8s |
| Hetzner CX22 + k3s | ~€4/mo | Self-hosted production cluster |
| DigitalOcean Kubernetes | $12/mo+ | Managed K8s (control plane free) |

> **Recommendation for this project:** Use `kind` locally — free, installs in 2 minutes, no cloud account needed.

---

### Quickest path to try Kubernetes locally

Both `kind` and `minikube` work equally well for this project. `kind` (Kubernetes IN Docker) starts faster and needs no VM — it runs K8s nodes inside Docker containers, making it lighter and popular for CI. `minikube` is more beginner-friendly, ships with optional addons (ingress controller, dashboard), and has a larger community. Use whichever feels more comfortable.

**macOS**

```bash
# kind
brew install kind kubectl

# minikube (alternative)
brew install minikube kubectl
```

**Windows (PowerShell)**

```powershell
# kind
winget install Kubernetes.kind
winget install Kubernetes.kubectl

# minikube (alternative)
winget install Kubernetes.minikube
winget install Kubernetes.kubectl
```

After installing, use one of the scenarios above:

- **`kind`**: build images → `kind create cluster` → `kind load docker-image ...` → `kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/`
- **`minikube`**: `minikube start` → `eval $(minikube docker-env)` → build images → `kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/`

> **Windows note:** run the `kind`/`minikube`, `kubectl`, `docker build`, and `docker compose` commands in **Git Bash** or **WSL** — the backgrounding operator `&` and the `open` command are not supported in PowerShell or CMD. Replace `open http://localhost:3000` with `start http://localhost:3000` (CMD) or just open the URL in your browser manually.

### Deploy to a cheap VPS with Docker Compose (no Kubernetes)

```bash
# On a fresh Ubuntu 24.04 VPS
curl -fsSL https://get.docker.com | sh
git clone https://github.com/evgenest/HotelPulse.git && cd HotelPulse
docker compose up -d
```

That's it — the full stack is running. No Kubernetes needed.

---

## Failure Demo

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
