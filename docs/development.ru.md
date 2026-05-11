# Гид разработчика

## Цель проекта

За **1-2 дня** собрать практический мини-проект, покрывающий максимум технологий современной hospitality-платформы. Объём намеренно минимален — достаточно, чтобы продемонстрировать реальные паттерны без overengineering.

Проект разработан в тематике hotel channel management и демонстрирует ключевые архитектурные паттерны для такого продукта: event-driven системы, data-driven architecture, разработка SaaS-платформы.

---

## День 1 — Backend + локальная инфра (≈6-8 ч)

### Шаг 1 — Скелет репо (15 мин)
```bash
mkdir hotelpulse && cd hotelpulse
git init
mkdir -p apps/api apps/worker apps/web k8s
```

### Шаг 2 — MongoDB + RabbitMQ через docker-compose (30 мин)

Запускаем только инфраструктуру:
```bash
docker compose up -d mongo rabbitmq
```

Проверяем: RabbitMQ Management UI открывается на `http://localhost:15672` (guest / guest).
Должны быть видны default vhost и пустой список очередей.

### Шаг 3 — .NET 10 API (2 ч)

Ключевые решения в `apps/api/Program.cs`:
- **Minimal API** (без контроллеров) — соответствует best practices .NET 10
- **MongoDB.Driver** для работы с базой
- **RabbitMQ.Client** напрямую, без MassTransit — учим примитив, не абстракцию
- Отели **засеиваются при первом запуске** — не нужно настраивать БД вручную
- `POST /api/bookings` возвращает **202 Accepted** мгновенно — не блокируется в ожидании worker

Используемые пакеты:
```xml
<PackageReference Include="MongoDB.Driver" Version="2.28.0" />
<PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
```

### Шаг 4 — .NET 10 Worker Service (1.5 ч)

Ключевые решения в `apps/worker/BookingConsumer.cs`:
- Наследуется от **BackgroundService** (паттерн hosted service в ASP.NET Core)
- Автоматически переподключается к RabbitMQ при сбое (retry loop в `ExecuteAsync`)
- `BasicQos(prefetchCount: 1)` — обрабатывает одно сообщение за раз, безопасно для демо
- **Не использует auto-ack** — сообщение подтверждается только после успешного обновления Mongo
- Имитирует 3-этапный пайплайн с реальными задержками (700мс + 800мс + 900мс)

### Шаг 5 — Dockerfile + docker-compose (30 мин)

Оба .NET проекта используют multi-stage builds:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

Runtime-образ `aspnet` значительно меньше `sdk` — важно для production-образов.

### Шаг 6 — Smoke test (30 мин)

```bash
docker compose up -d

# Создаём бронь
curl -s -X POST http://localhost:8080/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"hotelId":"h_alpina","roomId":"r1","guestName":"Test User","checkIn":"2026-07-01","checkOut":"2026-07-03","nights":2,"total":640}'

# Запоминаем id, ждём 3 секунды, затем:
curl http://localhost:8080/api/bookings/<id>
# status должен быть "confirmed" или "rejected"
```

В RabbitMQ UI на `localhost:15672` → Queues → `bookings.created` видим прохождение сообщения.

---

## День 2 — Frontend + Kubernetes (≈6-8 ч)

### Шаг 7 — Nuxt 4 фронтенд (3 ч)

Ключевые решения в `apps/web/`:
- **`ssr: false`** в `nuxt.config.ts` — SPA-режим, устраняет сложность с SSR/CSR URL-split
- Кастомный CSS без Tailwind — напрямую перенесён из прототипа Claude Design
- `useQueueStore.ts` — module-level reactive singleton для состояния Queue Visualizer
- `useBookingStore.ts` — история броней в `localStorage` (бэкенд не нужен)
- Polling через `setInterval` в `onMounted`, чистка в `onUnmounted`
- `definePageMeta({ layout: false })` на каждой странице — каждая страница управляет своей оболочкой

Три страницы:
| Страница | Маршрут | Ключевое поведение |
|---------|---------|-------------------|
| Список отелей | `/` | Загружает отели, рендерит сетку |
| Детали отеля | `/hotels/[id]` | Выбор номера, модальная форма бронирования |
| Статус брони | `/bookings/[id]` | Polling API каждые 1.5 сек до финального статуса |

### Шаг 8 — Полный docker-compose up (1 ч)

```bash
docker compose up --build -d
docker compose logs -f   # наблюдаем за запуском всех 5 сервисов
```

Обрати внимание на `depends_on` + healthcheck в `docker-compose.yml` — API и Worker стартуют только после того, как Mongo и RabbitMQ станут healthy.

### Шаг 9 — Kubernetes (2-3 ч)

#### Предварительные требования
- `kind`: `winget install Kubernetes.kind` (Windows) или `brew install kind` (Mac)
- `kubectl` установлен и настроен

#### Обзор манифестов

| Файл | Ресурс | Примечания |
|------|--------|-----------|
| `namespace.yaml` | Namespace `hotelpulse` | Изолирует все ресурсы |
| `mongo.yaml` | StatefulSet + Service | PersistentVolume для данных |
| `rabbitmq.yaml` | Deployment + Service | Readiness probe проверяет AMQP-порт |
| `api.yaml` | Deployment + Service | HTTP readiness probe на `/health` |
| `worker.yaml` | Deployment | Без Service — только консьюмит из очереди |
| `web.yaml` | Deployment + Service | Отдаёт Nuxt SPA |
| `ingress.yaml` | Ingress | Роутит `/api/*` → api, `/` → web |

#### Команды деплоя
```bash
# Сборка образов
docker build -t hotelpulse-api:dev    ./apps/api
docker build -t hotelpulse-worker:dev ./apps/worker
docker build -t hotelpulse-web:dev    ./apps/web

# Создаём кластер и загружаем образы
kind create cluster --name hotelpulse
kind load docker-image hotelpulse-api:dev    --name hotelpulse
kind load docker-image hotelpulse-worker:dev --name hotelpulse
kind load docker-image hotelpulse-web:dev    --name hotelpulse

# Деплой
kubectl apply -f k8s/

# Проверка статуса
kubectl get pods -n hotelpulse
kubectl get services -n hotelpulse
```

#### Доступ к приложению
```bash
kubectl port-forward svc/api 8080:8080 -n hotelpulse &
kubectl port-forward svc/web 3000:3000 -n hotelpulse &
```

### Шаг 10 — README (30 мин)

Уже готов. Все три сценария запуска — в корневом `README.md`.

---

## Верификация (чеклист)

### Уровень 1 — только API
```bash
curl http://localhost:8080/api/hotels | jq length        # → 6
curl http://localhost:8080/health                         # → {"status":"ok"}
```

### Уровень 2 — полный async-flow (docker-compose)
```bash
# 1. Создаём бронь
ID=$(curl -s -X POST http://localhost:8080/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"hotelId":"h_porter","roomId":"r2","guestName":"Test","checkIn":"2026-07-01","checkOut":"2026-07-03","nights":2,"total":390}' \
  | jq -r '.id')
echo "Создана: $ID"

# 2. Проверяем сразу — должно быть pending
curl -s http://localhost:8080/api/bookings/$ID | jq .status

# 3. Ждём 3 сек — должно стать confirmed или rejected
sleep 3 && curl -s http://localhost:8080/api/bookings/$ID | jq .status
```

### Уровень 3 — Frontend e2e
1. Открываем `http://localhost:3000`
2. Кликаем на отель → выбираем номер → заполняем форму → "Confirm booking"
3. На странице статуса наблюдаем анимацию пайплайна (api → queue → worker)
4. Статус меняется с `pending` на `confirmed` без перезагрузки страницы

### Уровень 4 — Failure-демо
```bash
# Останавливаем worker
docker compose stop worker

# Создаём бронь — статус навсегда зависнет в "pending"
curl -X POST http://localhost:8080/api/bookings ...

# Запускаем worker обратно — durable queue воспроизведёт сообщение
docker compose start worker
sleep 3
curl http://localhost:8080/api/bookings/<id>   # → confirmed
```

### Уровень 5 — Kubernetes
```bash
kubectl get pods -n hotelpulse               # все Running
kubectl port-forward svc/web 3000:3000 -n hotelpulse
# Повторяем e2e-тест Уровня 3
```

---

## Что намеренно не реализовано

| Функция | Почему исключено |
|---------|-----------------|
| Аутентификация | Значительно усложняет, нет обучающего эффекта для спайка |
| Реальные платежи | Вне скоупа демо |
| Unit-тесты | Цель спайка — интеграция, не покрытие |
| CI/CD | GitHub Actions — хороший следующий шаг, не нужен в день 1-2 |
| Pinia | Composables с module-level state достаточно для данного объёма |
| MassTransit | Голый `RabbitMQ.Client` лучше для обучения — меньше абстракций |

---

## Архитектурные заметки

- **RabbitMQ**: «Проект использует topic exchange с durable queue и manual ack. Worker использует `BasicQos(1)`, обрабатывая одно сообщение за раз — это безопасно для single-instance демо. Для production следующим шагом будут горизонтальное масштабирование Worker и dead-letter queue для неудачных сообщений.»

- **Async-архитектура**: «API возвращает 202 мгновенно, не ожидая подтверждения. Это соответствует data-driven подходу. Фронтенд делает polling, но для production'а естественный апгрейд — SSE или WebSockets.»

- **AI-assisted development**: «Claude использовался для ускорения скаффолдинга. Каждый файл всё равно был прочитан и понят вручную — AI берёт на себя шаблонный код, а архитектурные решения остаются явными.»
