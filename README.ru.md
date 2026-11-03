# HotelPulse

[![CI](https://github.com/evgenest/HotelPulse/actions/workflows/ci.yml/badge.svg)](https://github.com/evgenest/HotelPulse/actions/workflows/ci.yml)

HotelPulse — небольшой сервис бронирования отелей. Гость выбирает номер, API сразу принимает запрос, а воркер подтверждает заезд из устойчивой очереди. Один и тот же номер нельзя продать дважды на одни даты.

Это учебный проект. Цель — не просто работающий код и демо, а понять, как целиком устроена боевая система, от базы данных до браузера: какие решения влияют на надёжность, производительность и бизнес-правила и почему выбраны именно они. Эти решения и проблемы, из-за которых они появились, записаны в [`docs/adr/`](docs/adr/README.md), там же словарик терминов. Начать лучше с [`docs/why-not-mvp.md`](docs/why-not-mvp.md): все решения на одной странице и что сломалось бы без каждого из них.

Живое демо (бесплатный Render, Франкфурт; после простоя первый запрос может идти до минуты):

- Веб: https://hotelpulse-web.onrender.com — в шапке переключатель **.NET / Laravel**
- .NET API: https://hotelpulse-api.onrender.com ([`/health`](https://hotelpulse-api.onrender.com/health))
- Laravel API: https://hotelpulse-api-laravel.onrender.com ([`/health`](https://hotelpulse-api-laravel.onrender.com/health))

## Архитектура

```mermaid
flowchart LR
  web["Nuxt<br/>переключатель .NET / Laravel"] -->|HTTP| api[".NET API<br/>или Laravel API"]
  api -->|SQL| pg["PostgreSQL"]
  api -->|read-through| redis["Redis"]
  api -->|outbox| mq["RabbitMQ"]
  mq --> worker["Worker"]
  worker -->|confirm or reject| pg
  worker -->|invalidate| redis
  web -->|poll booking| api
```

Обе реализации работают по этой схеме; у каждой своя база, свои exchange и очередь и свой префикс ключей Redis, поэтому они работают одновременно и не мешают друг другу.

`POST /api/bookings` проверяет заезд, в одной транзакции пишет бронь и строку outbox и возвращает **202 Accepted** со `status: pending`. Релей публикует `booking.created` в устойчивую очередь: `bookings.created` у .NET (релей внутри API), `bookings.created.laravel` у Laravel (релей — отдельный процесс). Воркер двигает ленту событий и ставит `confirmed` или `rejected`. Страница опрашивает `GET /api/bookings/{id}`, пока статус не станет окончательным.

Если воркер остановлен, сообщение остаётся в RabbitMQ, а бронь остаётся `pending`. Запуск воркера её забирает. Повторная доставка уже завершённой брони подтверждается и игнорируется. Если Postgres отклоняет вставку из-за пересечения с другим активным заездом, API отвечает **409**.

Список отелей, карточка отеля и доступность кэшируются в Redis. Запись брони увеличивает версию ключа, поэтому следующее чтение идёт в PostgreSQL. Записи также живут 60 секунд. Ответы несут `X-Cache: HIT`, `MISS` или `BYPASS`.

## Две реализации одного API

Один и тот же HTTP-контракт ([`docs/api.ru.md`](docs/api.ru.md)) реализован дважды:

| | .NET | Laravel |
|---|---|---|
| Код | `apps/api`, `apps/worker`, `apps/HotelPulse.*` | `apps/api-laravel` |
| Среда | ASP.NET Core, .NET 10 | PHP 8.5, Laravel 13, Octane на FrankenPHP |
| Тесты | xUnit + Testcontainers | Pest, на настоящих Postgres, Redis и RabbitMQ |

Laravel-версия написана, чтобы освоить Laravel на проекте с уже выверенными правилами: сначала спецификация ([`docs/specs/laravel-port.md`](docs/specs/laravel-port.md)), затем код, с Claude Code. Обе версии держат одни и те же правила конкурентности в SQL ([`docs/adr/`](docs/adr/README.md)), проходят одни и те же сценарии тестов, включая параллельные, и используют одни и те же файлы схемы. У каждой своя база, очередь и ключи кэша, поэтому они работают одновременно. Если настроены обе, в шапке приложения есть переключатель **.NET / Laravel**.

## Стек

| Часть | Где |
|---|---|
| PHP 8.5 / Laravel 13 | `apps/api-laravel`: то же API, outbox relay и worker |
| C# / .NET 10 | `apps/api`, `apps/worker`, общие проекты домена и инфраструктуры |
| PostgreSQL 18 | Отели, номера, брони. SQL-миграции, exclusion-constraint, частичные индексы |
| Redis 8 | Кэш каталога и чтений доступности |
| RabbitMQ 4 | Устойчивая очередь `bookings.created`, ручной ack, transactional outbox |
| Nuxt 4 / Vue 3 / TypeScript | `apps/web` |
| xUnit | Юнит- и интеграционные тесты. Testcontainers локально, service containers в CI |
| Docker Compose | Локальный стек и `docker-compose.prod.yml` |
| Caddy | HTTPS на одном VPS |
| Kubernetes | Манифесты в `k8s/` |

Правило пересечения дат и снятые планы `EXPLAIN` — в [`docs/sql.md`](docs/sql.md). HTTP-контракт — в [`docs/api.ru.md`](docs/api.ru.md).

## Запуск

Docker Compose:

```bash
docker compose up -d --build

curl -fsS http://localhost:8080/health
curl -fsS http://localhost:8080/api/hotels
```

Laravel-API — в профиле `laravel`. Сначала ключ в `.env`:

```bash
echo "LARAVEL_APP_KEY=base64:$(openssl rand -base64 32)" >> .env
echo "NUXT_PUBLIC_API_BASE_LARAVEL=http://localhost:8081" >> .env
docker compose --profile laravel up -d --build
curl -fsS http://localhost:8081/health
```

Приложение: <http://localhost:3000>. Управление RabbitMQ: <http://localhost:15672> (`hotelpulse` / `hotelpulse`).

Миграции выполняются при старте API и вставляют шесть отелей. Более ранний checkout этого репозитория использовал MongoDB; `docker compose down -v` удаляет тот неиспользуемый volume вместе с данными Postgres.

Без Compose поднимите Postgres, Redis и RabbitMQ сами (`guest` на localhost подходит), затем:

```bash
dotnet run --project apps/api
dotnet run --project apps/worker
cd apps/web && pnpm install && pnpm dev
```

Laravel-API (PHP 8.5; задайте `APP_KEY`, `DATABASE_URL`, `REDIS_URL`, `AMQP_URL`) — три процесса:

```bash
cd apps/api-laravel && composer install
php artisan hotelpulse:migrate
php artisan octane:frankenphp --port=8081   # или: php artisan serve --port=8081
php artisan hotelpulse:relay
php artisan hotelpulse:consume
```

Kubernetes: соберите образы, загрузите их в `kind` или `minikube` и примените namespace раньше остальных манифестов.

```bash
docker build -t hotelpulse-api:dev    -f apps/api/Dockerfile .
docker build -t hotelpulse-worker:dev -f apps/worker/Dockerfile .
docker build -t hotelpulse-web:dev    -f apps/web/Dockerfile apps/web

kubectl apply -f k8s/namespace.yaml && kubectl apply -f k8s/
kubectl port-forward svc/api 8080:8080 -n hotelpulse
kubectl port-forward svc/web 3000:3000 -n hotelpulse
```

У Postgres есть постоянный volume. У Redis его нет: это кэш. Как volume ведёт себя на `kind` и `minikube`, описано в [`docs/kubernetes-storage-ru.md`](docs/kubernetes-storage-ru.md). Для одного VPS манифесты не нужны; см. [`DEPLOY.md`](DEPLOY.md).

## Тесты

```bash
npm run test:unit           # правила бронирования, без Docker
npm run test:integration    # API, воркер, Postgres, Redis, RabbitMQ
npm test                    # оба проекта
npm run typecheck           # Nuxt / Vue
```

Laravel (PHP 8.5 с `pdo_pgsql`, `redis`, `pcntl`, `sockets`, `bcmath`; задайте `DATABASE_URL`, `REDIS_URL`, `AMQP_URL`):

```bash
cd apps/api-laravel && composer install
./vendor/bin/pest              # unit + feature, включая параллельные запросы
./vendor/bin/pint --test       # стиль кода
./vendor/bin/phpstan analyse   # Larastan, уровень 8
```

Нагрузочный тест (k6 в Docker, оба API, пороги из спеки): [`docs/benchmarks.md`](docs/benchmarks.md).

Интеграционные тесты используют Testcontainers, если Docker доступен и `POSTGRES_URI` не задан. Задайте `POSTGRES_URI`, `REDIS_URI` и `RABBITMQ_URI`, чтобы направить их на уже запущенные сервисы. CI делает это через service containers и затем собирает три образа. См. [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

`HOTELPULSE_CONFIRM_PROBABILITY` по умолчанию `0.9`, поэтому живая бронь иногда отклоняется с причиной «Room became unavailable during processing.». Поставьте `1`, если каждый заезд должен подтверждаться. `HOTELPULSE_STEP_DELAY_MS` заменяет задержки 700 / 800 / 900 мс; тесты ставят `0`.

## Сбой воркера

```bash
docker compose stop worker

curl -s -X POST http://localhost:8080/api/bookings \
  -H 'Content-Type: application/json' \
  -d '{"hotelId":"h_porter","roomId":"r2","guestName":"Anna Schmidt","checkIn":"2027-04-01","checkOut":"2027-04-03","nights":2,"total":390}'

# статус остаётся pending — сообщение лежит в устойчивой очереди
docker compose start worker
# та же бронь переходит в confirmed
```

То же самое с сайта: остановите воркер, забронируйте номер, страница останется на «Processing», запустите воркер — опрос переключится на confirmed.

## Деплой

[`DEPLOY.md`](DEPLOY.md) описывает два пути. VPS — `docker-compose.prod.yml`, воркер в своём контейнере; этот файл и манифесты Kubernetes запускают только .NET-API, а Laravel-API работает через профиль Compose `laravel` локально и отдельным сервисом на Render. Бесплатное демо на Render — [`render.yaml`](render.yaml): .NET-API, Laravel-API и Nuxt как web-сервисы, Redis как Key Value, Postgres на Neon, RabbitMQ на CloudAMQP. На бесплатном плане Render нет background worker, поэтому Blueprint ставит `RUN_WORKER_IN_PROCESS=true` и очередь читает сам процесс .NET-API; Laravel-контейнер держит веб, релей и воркер вместе под supervisord. В Compose, Kubernetes и `apps/worker` этот флаг выключен. API понимает `DATABASE_URL`, `REDIS_URL`, `AMQP_URL` и `PORT`, а локальный стек по-прежнему задаёт `POSTGRES_URI`, `REDIS_URI` и `RABBITMQ_URI`. Репозиторий сам никуда не деплоится.

## Репозиторий

```
apps/api/                         HTTP API
apps/worker/                      потребитель очереди
apps/api-laravel/                 Laravel-реализация API, релея и воркера
docs/adr/                         архитектурные решения и словарик терминов
scripts/load/                     нагрузочный тест k6 и проверка базы после него
apps/web/                         приложение Nuxt
apps/HotelPulse.Domain/           валидация, переходы статусов, лента, политика доставки
apps/HotelPulse.Infrastructure/   SQL-миграции, репозитории, Redis, outbox, consumer
tests/                            юнит- и интеграционные тесты
k8s/                              namespace, Postgres, Redis, RabbitMQ, API, worker, web, ingress
deploy/Caddyfile                  прокси для продакшена
docker-compose.yml                локальный стек
docker-compose.prod.yml           продакшен на одном хосте
```

Учётные записи и оплата картой в этот сервис не входят. История в браузере — локальный кэш списка API; сами строки лежат в Postgres.
