# HotelPulse: Laravel-порт рядом с .NET (под вакансию Hurma, djinni 855560)

## Context
Пользователь хочет освоить PHP/Laravel на своём проекте и показать это в отклике (вакансия: Laravel + Vue, Spec-Driven Development, AI-агенты). Laravel для пользователя новый, и это не скрываем: README подаёт порт как «переписал свой .NET-проект на Laravel, чтобы освоить стек, по спекам и с Claude Code».

## Решения (зафиксированы с пользователем / рекомендованы)
- **Версии и политика обновлений:**
  - PHP: `"php": "~8.5.12"` в composer.json и образ `php:8.5-*`. Патчи 8.5.x приходят сами, переход на 8.6 — только вручную, после чтения changelog и прогона тестов.
  - Laravel: скелет v13.11.0, `"laravel/framework": "^13.39"` (сейчас последняя v13.39.0, вышла 2026-10-27). Минорные версии 13.x приходят через `composer update`.
  - Остальные PHP- и npm-пакеты: последние стабильные версии с caret-диапазоном (`^`). Lock-файлы коммитим, мажорные обновления — отдельными коммитами.
- **Структура (рекомендация, при approve можно поменять):** монорепо, новый `apps/api-laravel/` рядом с `apps/api`; один Nuxt-фронт с переключателем бэкенда .NET / Laravel; одни и те же контрактные тесты по `docs/api.md`.
- **Хостинг демо:**
  - Бесплатно: Render free web service, один Docker-контейнер, внутри которого `supervisord` (`nodaemon=true`) запускает FrankenPHP (слушает `$PORT`), `php artisan queue:work` и `php artisan hotelpulse:relay --loop`. Это аналог `RUN_WORKER_IN_PROCESS`.
  - Опционально, ~$2–5/мес: Fly.io, process groups `app` и `worker` в `fly.toml`, `release_command` = `migrate --force`. Бесплатного тарифа у Fly нет, только trial на 2 ч или 7 дней. Managed Postgres там стоит $38, поэтому используем Neon.
  - Laravel Cloud не используем: Starter стоит $5/мес плюс usage, бесплатного тарифа нет.
- **Внешние сервисы остаются:** Neon, CloudAMQP, Render Key Value. Изоляция от .NET-стека: отдельная ветка или база Neon (`DATABASE_URL` своя), отдельная очередь `bookings.laravel` (или vhost), префикс Redis `laravel:`.
- **Замеры** (если пользователь подтвердит): время ответа API и время от 202 до confirmed для каждого бэкенда, с подписью, что на free-тарифах это в основном сеть и холодный старт.

## Этап 0: фиксы конкурентности в .NET (до порта, отдельные `fix:`-коммиты)
Аудит показал, что правило из CLAUDE.md («конкурентность в SQL») архитектурно верное и в .NET в целом соблюдается:
- создание бронирования защищено exclusion constraint (23P01 → 409);
- отмена сделана как read → decide → CAS с теми же условиями в WHERE, плюс повтор;
- worker использует `WHERE status = 'pending'`;
- идемпотентность держится на PK + перехвате unique violation;
- relay берёт строки через `FOR UPDATE SKIP LOCKED`.

Найденные дыры:
1. **Lost update таймлайна** (`BookingRepository.TryCancelAsync`). Отмена записывает `events`, посчитанные из прочитанной строки. Если worker между чтением и записью добавил шаги delivered/locked (статус всё ещё pending, CAS проходит), эти шаги теряются. Фикс: добавлять событие в SQL, `events = events || @cancelEvent::jsonb` (`BookingTimeline.Cancelled` и так только добавляет событие в конец).
2. **Outbox без publisher confirms** (`BookingPublisher`). `published_at` проставляется, хотя брокер мог не принять сообщение. Тогда бронирование навсегда зависает в pending. Фикс: `CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)` и `mandatory: true`. Публикация без подтверждения → строка остаётся неопубликованной.
3. **Нет теста на настоящую параллельность.** `An_overlapping_stay_is_rejected...` проверяет запросы последовательно. Добавить: N параллельных POST на одну комнату и даты → ровно один 202, остальные 409. Параллельные отмена и подтверждение worker'ом → одно итоговое состояние, таймлайн полный.

Для Laravel те же правила:
- **Запрещено:** `$booking->status = ...; $booking->save()`.
- **Разрешено:** query builder / Eloquent `->where('status', ...)->update([...])` с проверкой affected rows; `DB::selectOne('UPDATE ... RETURNING ...')`.
- `QueryException` с SQLSTATE `23P01` → 409.

Те же три теста пишутся на Pest.

## Порядок работы (по CLAUDE.md: spec first)
1. `docs/specs/laravel-port.md`: цель, контракт = `docs/api.md`, таблица кейсов → статусы, изоляция данных, хостинг, тесты. Ревью спеки пользователем.
2. Ветка `claude/epic-cori-g8qizf`. `composer create-project laravel/laravel apps/api-laravel` в Docker-образе `php:8.5.12`.
3. Domain: перенести `apps/HotelPulse.Domain/BookingValidator.cs`, `BookingCancellation.cs`, `BookingProcessor.cs`, `BookingStatuses.cs`, `DeliveryPolicy.cs`, `IdempotencyHash.cs` в PHP enums и final-классы со статическими методами. Тесты на Pest переносятся из `tests/HotelPulse.UnitTests/*`.
4. Миграции: SQL из `apps/HotelPulse.Infrastructure/Migrations/001–003` дословно через `DB::unprepared`, включая exclusion constraint и `bookings_availability_idx`.
5. Репозитории: compare-and-set `WHERE status = ...` как в `Postgres/BookingRepository.cs` (после фиксов этапа 0), availability как в `AvailabilitySql.cs`. Изменения статуса только условным `update()` или `UPDATE ... RETURNING`, без `save()` модели.
6. HTTP: `routes/api.php`, контроллеры и FormRequest повторяют `apps/api/Endpoints.cs`; заголовок `X-Cache`; Redis-сбой → BYPASS (try/catch); `InvalidateHotel` после каждой записи, меняющей доступность.
7. Outbox relay: artisan-команда. Consumer: Laravel queue на RabbitMQ (`vladimir-yuldashev/laravel-queue-rabbitmq`, если поддерживает Laravel 13, иначе `php-amqplib` напрямую). Job идемпотентен: уже завершённое бронирование → ack и пропуск.
8. Интеграционные тесты: сценарии `BookingFlowTests` / `CancellationTests` против реальных Postgres/Redis/RabbitMQ; в CI добавить job для PHP.
9. Фронт: переключатель бэкенда (`NUXT_PUBLIC_API_BASE_DOTNET` / `_LARAVEL`), выбор хранится в localStorage; `npm run typecheck`.
10. Деплой: Dockerfile + supervisord.conf, сервис в `render.yaml`; опционально `fly.toml`. Документация: `docs/api.md` + `docs/api.ru.md`, README/README.ru, DEPLOY.md, CHANGELOG; версия в `Directory.Build.props` и `apps/web/package.json`.

## Проверка
- `./vendor/bin/pest` (unit + integration) зелёный, `npm test`, `npm run typecheck`, CI зелёный.
- `docker compose up -d --build`: обе API отвечают одинаково на одни и те же запросы; бронирование через каждый бэкенд доходит до `confirmed`; два параллельных бронирования одной комнаты → одно 202 и одно 409 в обоих стеках.
- Render: контейнер слушает `$PORT`, worker обрабатывает очередь после пробуждения.
