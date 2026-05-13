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
| `web`      | Nuxt 4 (Node 24 + pnpm)      | `:3000`          |
| `api`      | ASP.NET Core 10 Minimal API  | `:8080`          |
| `worker`   | .NET 10 Worker Service       | —                |
| `mongo`    | MongoDB 8                    | `:27017`         |
| `rabbitmq` | RabbitMQ 4 + Management UI   | `:5672` `:15672` |

---

## Технологии проекта

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
| AI-assisted dev  | Использовался для ускорения скаффолдинга и шаблонных частей проекта    |

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

Если вы обновляете существующий локальный volume с MongoDB 7 до MongoDB 8, перед первым запуском пересоздайте volume:

```bash
docker compose down -v
docker compose up -d
```

API заново заполняет коллекцию `hotels` при старте, поэтому каталог отелей восстановится автоматически на чистом volume.

### Вариант 2 — Локально без Docker

Нужно: .NET 10 SDK, Node 24, pnpm, MongoDB на 27017, RabbitMQ на 5672.

```bash
# Терминал 1 — API
cd apps/api
dotnet run

# Терминал 2 — Worker
cd apps/worker
dotnet run

# Терминал 3 — Frontend
cd apps/web
pnpm install
pnpm run dev
```

### Вариант 3 — Kubernetes (`kind` или `minikube`)

#### Вариант 3A — `kind`

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

# 3. Деплой: сначала namespace, затем остальные манифесты
kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # ждём пока все Running

# 4. Доступ
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

#### Вариант 3B — `minikube`

```bash
# 1. Запускаем кластер
minikube start

# 2. Переключаем Docker CLI на daemon внутри minikube
eval $(minikube docker-env)

# 3. Сборка образов внутри minikube
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

# 4. Деплой: сначала namespace, затем остальные манифесты
kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/
kubectl get pods -n hotelpulse          # ждём пока все Running

# 5. Доступ
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
open http://localhost:3000
```

> Если после деплоя Pod'ы застряли в `ImagePullBackOff`, скорее всего, образы были собраны не внутри Docker daemon'а `minikube`. В этом случае снова выполни `eval $(minikube docker-env)` и пересобери все три образа.

Если в кластере уже есть PVC с данными MongoDB 7, перед применением `mongo:8` используйте свежий PVC или новый namespace.
Самый простой путь здесь — чистый PVC; каталог отелей будет заново заполнен при старте API.

---

## Руководство по развёртыванию

### Нужна ли виртуальная машина (например Hetzner) для запуска проекта?

**Нет** — для разработки и демонстрации всё запускается локально через Docker Compose (Вариант 1 выше), без какого-либо сервера.

Если хочется опубликовать проект в интернете (например, для портфолио), достаточно одного дешёвого VPS:

| Провайдер | Самый дешёвый тариф | Стоимость в месяц |
|-----------|---------------------|-------------------|
| Hetzner | CX22 (2 vCPU, 4 ГБ RAM) | ~€4 |
| DigitalOcean | Basic Droplet (1 vCPU, 1 ГБ RAM) | $6 |
| Oracle Cloud | Always Free (2 VM) | Бесплатно |

Достаточно установить Docker на сервер и запустить `docker compose up -d`. **Kubernetes для простого развёртывания не нужен.**

---

### Для чего здесь нужен Kubernetes и какую функцию выполняет?

В реальной компании Kubernetes управляет оркестрацией контейнеров на множестве серверов. В этом демо-проекте манифесты в `k8s/` показывают, *как тот же набор сервисов разворачивается в production-среде*:

| Что | Какой инструмент Kubernetes |
|-----|-----------------------------|
| Автоматически перезапускает API при падении | `Deployment` с политикой перезапуска |
| Помечает RabbitMQ и API как готовые и добавляет их в endpoints сервисов только после успешной проверки | Readiness probes |
| Даёт каждому сервису стабильное внутреннее имя | `Service` (ClusterIP) |
| Обеспечивает постоянное хранилище для MongoDB | `PersistentVolumeClaim` |
| Роутит `/api/*` → API, `/` → Web через HTTP | `Ingress` (требует nginx ingress controller) |
| Изолирует все ресурсы от других рабочих нагрузок | `Namespace` `hotelpulse` |

Readiness probes не задают порядок запуска других Pod'ов: `worker` не "ждёт" RabbitMQ на уровне Kubernetes и стартует сразу, полагаясь на логику переподключения приложения.

На одной машине Docker Compose уже делает всё то же самое. Папка `k8s/` демонстрирует знание production-пути — именно это важно показать на интервью.

---

### Бесплатные и дешёвые варианты Kubernetes

| Вариант | Стоимость | Для чего |
|---------|-----------|----------|
| `kind` на ноутбуке | **Бесплатно** | Локальная разработка / демо (этот проект) |
| `minikube` на ноутбуке | **Бесплатно** | Локальная разработка / демо |
| `k3s` на дешёвом VPS | ~€4/мес (только сервер) | Лёгкий self-hosted Kubernetes |
| Docker Desktop (встроенный K8s) | **Бесплатно** | Windows / Mac |
| Oracle Cloud Free Tier | **Бесплатно** | Облачный Kubernetes без оплаты |
| Hetzner CX22 + k3s | ~€4/мес | Self-hosted production-кластер |
| DigitalOcean Kubernetes | от $12/мес | Managed Kubernetes |

> **Рекомендация для этого проекта:** Используй `kind` локально — бесплатно, устанавливается за 2 минуты, облачный аккаунт не нужен.

---

### Быстрый старт Kubernetes локально

`kind` и `minikube` одинаково подходят для этого проекта. `kind` (Kubernetes IN Docker) стартует быстрее и не требует виртуальной машины — K8s-узлы запускаются прямо в Docker-контейнерах, что делает его легче и удобнее для CI. `minikube` дружелюбнее для новичков, поставляется с опциональными аддонами (ingress controller, dashboard) и имеет большее сообщество. Выбирай любой.

**macOS**

```bash
# kind
brew install kind kubectl

# minikube (альтернатива)
brew install minikube kubectl
```

**Windows (PowerShell)**

```powershell
# kind
winget install Kubernetes.kind
winget install Kubernetes.kubectl

# minikube (альтернатива)
winget install Kubernetes.minikube
winget install Kubernetes.kubectl
```

После установки используй один из сценариев выше:

- **`kind`**: сборка образов → `kind create cluster` → `kind load docker-image ...` → `kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/`
- **`minikube`**: `minikube start` → `eval $(minikube docker-env)` → сборка образов → `kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/`

> **Windows:** команды `kind`/`minikube`, `kubectl`, `docker build` и `docker compose` выполняй в **Git Bash** или **WSL** — оператор `&` и команда `open` не поддерживаются в PowerShell/CMD. Вместо `open http://localhost:3000` используй `start http://localhost:3000` (CMD) или просто открой ссылку в браузере вручную.

### Деплой на дешёвый VPS через Docker Compose (без Kubernetes)

```bash
# На чистом Ubuntu 24.04 VPS
curl -fsSL https://get.docker.com | sh
git clone https://github.com/evgenest/HotelPulse.git && cd HotelPulse
docker compose up -d
```

Готово — весь стек работает. Kubernetes не нужен.

---

## Failure-демо

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
