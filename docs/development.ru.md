# Разработка

## Структура

```
apps/HotelPulse.Domain/           правила бронирования, без ввода-вывода
apps/HotelPulse.Infrastructure/  PostgreSQL, Redis, RabbitMQ, SQL-миграции
apps/api/                        HTTP API и outbox-релей
apps/worker/                     потребитель очереди
apps/web/                        приложение Nuxt
tests/HotelPulse.UnitTests/
tests/HotelPulse.IntegrationTests/
```

`POST /api/bookings` проверяет заезд, в одной транзакции пишет бронь и строку outbox и возвращает `202`. Релей публикует `booking.created` в устойчивую очередь `bookings.created`. Воркер идемпотентен: повторная доставка уже подтверждённой или отклонённой брони подтверждается и игнорируется. Пересекающиеся активные заезды отклоняет exclusion-constraint (`409`).

## Тесты

Юнит-тесты не требуют Docker:

```bash
npm run test:unit
```

Интеграционные тесты работают с настоящими Postgres, Redis и RabbitMQ. Если Docker запущен и переменные подключения не заданы, их поднимает Testcontainers. В CI те же тесты используют сервис-контейнеры из `.github/workflows/ci.yml`, когда заданы `POSTGRES_URI`, `REDIS_URI` и `RABBITMQ_URI`.

```bash
npm run test:integration
npm test
npm run typecheck
```

`HOTELPULSE_STEP_DELAY_MS=0` и `HOTELPULSE_CONFIRM_PROBABILITY=1` делают конвейер мгновенным и детерминированным. Без них шаги длятся 700, 800 и 900 мс, а подтверждение выпадает в 90% случаев.

`DATABASE_URL`, `REDIS_URL` и `AMQP_URL` перекрывают `POSTGRES_URI`, `REDIS_URI` и `RABBITMQ_URI`. `RUN_WORKER_IN_PROCESS=true` запускает потребителя внутри процесса API; без флага очередь читает `apps/worker`. `PORT` — HTTP-порт, если его задаёт хост вроде Render.

## Процессы без Compose

Нужны Postgres, Redis и RabbitMQ на значениях по умолчанию из `HotelPulseSettings` (localhost, база `hotelpulse` / `hotelpulse`, RabbitMQ `guest` на localhost). Дальше:

```bash
dotnet run --project apps/api
dotnet run --project apps/worker
cd apps/web && pnpm install && pnpm dev
```

API применяет миграции при старте. Его нужно запустить раньше воркера.

## SQL

Выбор индексов и снятые планы `EXPLAIN` — в [`docs/sql.md`](sql.md).

## Фронтенд

`apps/web` — SPA на Nuxt 4. `pnpm typecheck` (или `pnpm test`) запускает `nuxt typecheck`. Форма отправляет ночи и сумму; API пересчитывает их из цены номера и отклоняет несовпадение.
