# API

Базовый URL в разработке: `http://localhost:8080` (.NET) или `http://localhost:8081` (Laravel, `--profile laravel`). Обе реализации следуют этому контракту; изменение здесь меняет обе.

В ответах есть `X-Request-Id` (берётся из запроса или создаётся). `GET /health/live` — liveness, без зависимостей. `POST /api/bookings` отвечает `429` с `Retry-After`, если один клиент шлёт больше 30 бронирований в минуту (`HOTELPULSE_BOOKING_RATE_LIMIT`, `0` выключает); если Redis недоступен, лимит пропускается.

Даты — `yyyy-MM-dd`. Деньги — число JSON. `createdAt` — UTC.

## Отели

### `GET /api/hotels`

Шесть отелей и их номера, по имени. Ответ кэшируется. Заголовок `X-Cache`: `HIT`, `MISS` или `BYPASS`.

### `GET /api/hotels/{id}`

Один отель. `404`, если id неизвестен. Тот же заголовок кэша.

### `GET /api/hotels/{id}/availability?checkIn=2027-04-01&checkOut=2027-04-03`

У каждого номера `available: true`, если ни один pending или confirmed заезд не пересекает это полуоткрытое окно. `404`, если отеля нет. `400`, если дат нет или выезд не позже заезда. Кэш на отель и пару дат; бронь этого отеля его сбрасывает.

```json
{
  "hotelId": "h_porter",
  "checkIn": "2027-04-01",
  "checkOut": "2027-04-03",
  "rooms": [
    { "id": "r1", "type": "Courtyard Single", "capacity": 1, "price": 150, "sqm": 14, "available": true }
  ]
}
```

SQL и план — в [`sql.md`](sql.md).

## Брони

### `POST /api/bookings`

Заезд не подтверждается синхронно. Тело проверяется, пишется `pending`, в outbox ставится сообщение, ответ — **202 Accepted**.

```json
{
  "hotelId": "h_porter",
  "roomId": "r2",
  "guestName": "Anna Schmidt",
  "checkIn": "2027-04-01",
  "checkOut": "2027-04-03",
  "nights": 2,
  "total": 390
}
```

`nights` равно числу ночей между датами. `total` равен цене номера, умноженной на это число. Имя гостя обязательно и не длиннее 120 символов. Заезд не может быть раньше сегодняшнего дня (UTC).

**202**

```json
{ "id": "bkg_a3f9c12d8e4b", "status": "pending" }
```

Необязательный заголовок `Idempotency-Key` (до 200 символов). Тот же ключ и то же тело возвращают исходную бронь. Тот же ключ с другим телом — **409**.

**400** — ошибки проверки: `{ "errors": [{ "field": "total", "message": "..." }] }`.

**404** — неизвестный отель или номер.

**409** — эти даты уже удерживает pending или confirmed заезд этого номера.

### `GET /api/bookings`

20 последних броней, сначала новые.

### `GET /api/bookings?ids={id1},{id2}`

До 20 броней в запрошенном порядке.

### `GET /api/bookings/{id}`

Опрашивайте, пока `status` не станет `confirmed` или `rejected`.

```json
{
  "id": "bkg_a3f9c12d8e4b",
  "hotelId": "h_porter",
  "hotelName": "The Porter",
  "roomId": "r2",
  "roomType": "Standard Queen",
  "guestName": "Anna Schmidt",
  "checkIn": "2027-04-01",
  "checkOut": "2027-04-03",
  "nights": 2,
  "total": 390,
  "status": "confirmed",
  "confirmationCode": "HP-A3F9C1",
  "createdAt": "2026-11-02T14:32:00Z",
  "events": [
    { "label": "api → POST /bookings received", "done": true, "time": "14:32:00", "current": false },
    { "label": "api → published to bookings.created", "done": true, "time": "14:32:00", "current": false },
    { "label": "worker → message delivered", "done": true, "time": "14:32:01", "current": false },
    { "label": "worker → reservation locked in postgres", "done": true, "time": "14:32:02", "current": false },
    { "label": "worker → status: confirmed", "done": true, "time": "14:32:02", "current": false }
  ]
}
```

Отказ ставит `rejectionReason` и оставляет `confirmationCode` пустым. Примерно одна бронь из десяти отклоняется, если не задано `HOTELPULSE_CONFIRM_PROBABILITY=1`.

### `POST /api/bookings/{id}/cancel`

Отменяет бронь в статусе `pending` или `confirmed` до даты заезда. Тело запроса не нужно. Полные правила — в [`specs/booking-cancellation.md`](specs/booking-cancellation.md).

| Случай | Ответ |
|---|---|
| Бронь отменена | `200` с бронью: `status: "cancelled"`, `cancelledAt` и событие `api → booking cancelled` |
| Уже отменена | `200` с бронью без изменений |
| Неизвестный id | `404` |
| Бронь в статусе `rejected` | `409` |
| Заезд сегодня или раньше (UTC) | `409` |

Отменённая бронь больше не держит номер, кеш чтений по отелю сбрасывается.

### `GET /health`

`200`, когда Postgres отвечает на `SELECT 1`. Иначе `503`. Redis для readiness не нужен. Тело ответа: `{ status, version, time }`.

## Очередь

Обмен `hotelpulse` (topic, durable). Ключ `booking.created`. Очередь `bookings.created` (durable). Сообщения persistent.

```json
{
  "bookingId": "bkg_a3f9c12d8e4b",
  "hotelId": "h_porter",
  "roomId": "r2",
  "createdAt": "2026-11-02T14:32:00Z"
}
```

Строка фиксируется до публикации. Если брокер недоступен, outbox-релей повторяет попытку. Воркер подтверждает сообщение только после терминального статуса. Сбой посередине конвейера приводит к повторной доставке; смена статуса — compare-and-set из `pending`, поэтому второй проход не выдаёт второй код. Ядовитое тело отбрасывается. Временная ошибка возвращается в очередь один раз, затем бронь отклоняется. Недоступность базы возвращает сообщение в очередь даже при второй доставке, чтобы оно не пропало, пока Postgres недоступен.

Конвейер ждёт 700 мс, затем 800 мс, затем 900 мс, если не задан `HOTELPULSE_STEP_DELAY_MS`.

## Таблицы

`hotels`, `rooms`, `bookings`, `outbox`, `idempotency_keys` и `schema_migrations`. Их создаёт `apps/HotelPulse.Infrastructure/Migrations`. Каталог — `002_seed.sql`.
