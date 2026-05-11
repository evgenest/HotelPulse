# HotelPulse

Мини-SaaS для бронирования отелей, покрывающий полный стек современной hospitality-платформы в одном проекте: **.NET 10 · Vue/Nuxt 4 · RabbitMQ · MongoDB · Docker · Kubernetes**.

Основная идея: подтверждение брони **асинхронное**. `POST /api/bookings` сразу возвращает `202 Accepted` со статусом `pending`, публикует сообщение в RabbitMQ, и отдельный Worker Service обрабатывает его — фронтенд при этом опрашивает API до получения `confirmed` или `rejected`.

---

## Архитектура

```
[Nuxt 4 SPA]  ── HTTP ──►  [.NET 10 Web API]  ──► [MongoDB]
                                    │
                                    └── publish ──► [RabbitMQ]  ──► [.NET Worker]
                                                                          │
                                                                          ▼
                                                                    [MongoDB update]
```

### Сервисы (5 контейнеров)

| Контейнер  | Технология                   | Порты            |
|------------|------------------------------|------------------|
| `web`      | Nuxt 4 (Node 22)             | `:3000`          |
| `api`      | ASP.NET Core 10 Minimal API  | `:8080`          |
| `worker`   | .NET 10 Worker Service       | —                |
| `mongo`    | MongoDB 8                    | `:27017`         |
| `rabbitmq` | RabbitMQ 4 + Management UI   | `:5672` `:15672` |

---

## Технологии из вакансии

| Технология       | Где используется                                                       |
|------------------|------------------------------------------------------------------------|
| C# / .NET 10     | `apps/api` (Minimal API) + `apps/worker` (BackgroundService)          |
| REST API         | `/api/hotels`, `/api/bookings`                                         |
| RabbitMQ         | Очередь `bookings.created` между API и Worker                         |
| Vue 3 / Nuxt 4   | Frontend SPA                                                           |
| TypeScript       | Весь фронтенд                                                          |
| MongoDB          | Коллекции: `hotels`, `bookings`                                        |
| Docker           | Dockerfile на каждый сервис + `docker-compose.yml`                     |
| Kubernetes       | 7 манифестов в `k8s/` — запускать через `kind` или Docker Desktop K8s |
| AI-assisted dev  | Весь проект собран с Claude — стоит упомянуть на интервью              |

---

## Структура репозитория

```
hotelpulse/
├── docker-compose.yml
├── README.md
├── README.ru.md
├── docs/
│   ├── api.md                # API-контракт + формат сообщений RabbitMQ
│   ├── api.ru.md
│   ├── development.md        # Пошаговый план + верификация
│   └── development.ru.md
├── apps/
│   ├── api/                  # ASP.NET Core 10 Minimal API
│   │   ├── HotelPulse.Api.csproj
│   │   ├── Program.cs        # все эндпоинты + DI + seed-данные
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
│       │   ├── components/       # 11 Vue компонентов
│       │   └── pages/
│       │       ├── index.vue           # список отелей
│       │       ├── hotels/[id].vue     # детали отеля + форма брони
│       │       └── bookings/[id].vue   # статус брони (polling)
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

## Быстрый старт

### Вариант 1 — Docker Compose (рекомендуется для первого запуска)

```bash
docker compose up -d

# проверяем
curl http://localhost:8080/api/hotels          # список 6 отелей
curl http://localhost:8080/health              # {"status":"ok"}
open http://localhost:3000                     # фронтенд
open http://localhost:15672                    # RabbitMQ UI (guest / guest)
```

### Вариант 2 — Локально без Docker

Нужно: .NET 10 SDK, Node 22, MongoDB на 27017, RabbitMQ на 5672.

```bash
# Терминал 1 — API
cd apps/api
dotnet run

# Терминал 2 — Worker
cd apps/worker
dotnet run

# Терминал 3 — Frontend
cd apps/web
npm install
npm run dev
```

### Вариант 3 — Kubernetes (kind)

```bash
# 1. Сборка образов
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

# 2. Создаём кластер и загружаем образы
kind create cluster --name hotelpulse
kind load docker-image hotelpulse-api:dev    --name hotelpulse
kind load docker-image hotelpulse-worker:dev --name hotelpulse
kind load docker-image hotelpulse-web:dev    --name hotelpulse

# 3. Деплой
kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # ждём пока все Running

# 4. Доступ
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

---

## Failure-демо (отличная тема для интервью)

```bash
# Останавливаем worker — бронь зависнет в статусе "pending"
docker compose stop worker

# Создаём бронь в UI — статус не меняется
# Запускаем worker обратно — RabbitMQ воспроизведёт сообщение из durable queue
docker compose start worker
```

Это демонстрирует главную ценность очереди: **устойчивость при частичном сбое**.

---

## Что намеренно не реализовано

Следующее было сознательно исключено, чтобы уложиться в 1-2 дня:

- Аутентификация / авторизация
- Реальные платежи
- Unit / интеграционные тесты
- CI/CD пайплайн
- Pinia и сложный state management (composables достаточно)

Подробный пошаговый план — в [`docs/development.ru.md`](docs/development.ru.md).
API-контракт и формат сообщений — в [`docs/api.ru.md`](docs/api.ru.md).
