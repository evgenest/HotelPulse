# ADR-0003. Событие отмены дописывается в таймлайн внутри UPDATE

**Статус:** принято 2026-11-03 (коммит `1f9bba9`).

## Контекст
У бронирования есть колонка `events jsonb`: таймлайн для UI («получено → опубликовано → доставлено → комната заблокирована → статус»). Worker по ходу обработки дописывает шаги (`UpdateEventsAsync ... WHERE status = 'pending'`). Отмена добавляет в конец событие «booking cancelled».

## Проблема: lost update
Раньше отмена работала так:
1. API читает строку: `status = pending`, `events = [received, published]`.
2. Worker дописывает шаг: `events = [received, published, delivered]`. Статус по-прежнему `pending`.
3. API пишет `events = [received, published, cancelled]`, то есть список, посчитанный **из своей старой копии**. Compare-and-set `WHERE status IN ('pending','confirmed')` проходит, потому что статус не менялся.

Шаг `delivered` пропал. Это классический **lost update**: compare-and-set защищал колонку `status`, но не `events`.

## Решение
Новый таймлайн считается **внутри UPDATE из того, что лежит в строке сейчас**:

```sql
events = (SELECT coalesce(jsonb_agg(e || '{"current": false}' ORDER BY ord), '[]')
          FROM jsonb_array_elements(bookings.events) WITH ORDINALITY AS t(e, ord))
         || jsonb_build_array(@cancelEvent::jsonb)
```

API передаёт только новое событие, а не весь список. Сигнатура `TryCancelAsync(id, today, BookingEvent cancelEvent, ct)`.

## Рассмотренные альтернативы
- **Колонка версии (`version int`, optimistic locking)** — `WHERE version = @v`, при несовпадении повторять. Это универсальный способ, но нужна миграция и повторы, а нам достаточно дописать элемент в конец.
- **`SELECT ... FOR UPDATE` перед отменой** — тоже работает, но держит блокировку на время всей логики в C#.
- **Отдельная таблица `booking_events` (append-only)** — самый чистый вариант для настоящего аудита: каждая запись — новая строка, конфликтов нет. Для демо это лишняя миграция; вариант на будущее.

## Последствия
- Отмена больше не может стереть шаги worker'а. Проверяет тест `Cancelling_keeps_worker_steps_written_after_the_api_read_the_row`.
- Логика «сбросить `current` и дописать событие» теперь есть в двух местах: `BookingTimeline.Cancelled` (C#, unit-тесты) и SQL. При изменении нужно править оба.

## Как объяснить на собеседовании
«Compare-and-set защищает только колонки из WHERE. Если UPDATE пишет другую колонку, посчитанную из старого чтения, получаешь lost update. Есть три способа: считать новое значение в самом UPDATE (`events = events || ...`), версия строки или блокировка. Я выбрал первый: он без повторов и без миграции.»
