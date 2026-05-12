# API Reference

## REST-эндпоинты

### Отели

#### `GET /api/hotels`
Возвращает список всех отелей вместе с номерами.

**Ответ `200 OK`**
```json
[
  {
    "id": "h_alpina",
    "name": "Hôtel Alpina",
    "city": "Zermatt, CH",
    "rating": 5,
    "glyph": "ALP-01",
    "priceFrom": 320,
    "description": "...",
    "amenities": ["Spa", "Restaurant", "Ski storage"],
    "rooms": [
      { "id": "r1", "type": "Mountain Standard", "capacity": 2, "price": 320, "sqm": 22 }
    ]
  }
]
```

#### `GET /api/hotels/{id}`
Возвращает один отель по идентификатору.

**Ответ `200 OK`** — та же структура (один объект).
**Ответ `404`** — отель не найден.

---

### Бронирования

#### `POST /api/bookings`
Создаёт бронь и публикует сообщение в RabbitMQ.

**API не обрабатывает бронь синхронно.** Он только записывает запись со статусом `pending` в MongoDB и сразу публикует сообщение в очередь. Worker подберёт его в течение 2-3 секунд.

**Тело запроса**
```json
{
  "hotelId":   "h_alpina",
  "roomId":    "r2",
  "guestName": "Anna Schmidt",
  "checkIn":   "2026-06-01",
  "checkOut":  "2026-06-05",
  "nights":    4,
  "total":     1920
}
```

**Ответ `202 Accepted`**
```json
{ "id": "bkg_a3f9c12d8e4b", "status": "pending" }
```

#### `GET /api/bookings?ids={id1},{id2}`
Возвращает текущее состояние до 20 известных броней, сохраняя порядок запрошенных идентификаторов. Боковая панель истории во фронтенде использует этот эндпоинт, чтобы синхронизировать локально сохраненную историю с данными бронирований из MongoDB без отдельного запроса на каждую запись.

**Ответ `200 OK`**
```json
[
  {
    "id": "bkg_a3f9c12d8e4b",
    "hotelId": "h_alpina",
    "hotelName": "Hôtel Alpina",
    "roomId": "r2",
    "roomType": "Matterhorn View King",
    "guestName": "Anna Schmidt",
    "checkIn": "2026-06-01",
    "checkOut": "2026-06-05",
    "nights": 4,
    "total": 1920,
    "status": "pending",
    "createdAt": "2026-05-11T14:32:00Z",
    "events": []
  }
]
```

#### `GET /api/bookings/{id}`
Возвращает текущее состояние брони. Вызывается в цикле для отслеживания подтверждения.

**Ответ `200 OK`**
```json
{
  "id": "bkg_a3f9c12d8e4b",
  "hotelId": "h_alpina",
  "hotelName": "Hôtel Alpina",
  "roomId": "r2",
  "roomType": "Matterhorn View King",
  "guestName": "Anna Schmidt",
  "checkIn": "2026-06-01",
  "checkOut": "2026-06-05",
  "nights": 4,
  "total": 1920,
  "status": "confirmed",
  "confirmationCode": "HP-A3F9C1",
  "rejectionReason": null,
  "createdAt": "2026-05-11T14:32:00Z",
  "events": [
    { "label": "api → POST /bookings received",          "done": true,  "time": "14:32:00", "current": false },
    { "label": "api → published to bookings.created",    "done": true,  "time": "14:32:00", "current": false },
    { "label": "worker → message delivered",             "done": true,  "time": "14:32:01", "current": false },
    { "label": "worker → reservation locked in mongo",   "done": true,  "time": "14:32:02", "current": false },
    { "label": "worker → status: confirmed",             "done": true,  "time": "14:32:02", "current": false }
  ]
}
```

**Возможные значения `status`:** `pending` · `confirmed` · `rejected`

**Ответ `404`** — бронь не найдена.

#### `GET /health`
Liveness-проба для Kubernetes.

**Ответ `200 OK`**
```json
{ "status": "ok", "time": "2026-05-11T14:32:00Z" }
```

---

## Стратегия polling

Фронтенд опрашивает `GET /api/bookings/{id}` каждые **1500 мс** до тех пор, пока `status !== "pending"`, затем останавливается. Без WebSocket и SSE — просто и достаточно для демо.

```typescript
// apps/web/pages/bookings/[id].vue
onMounted(async () => {
  await fetchBooking()
  if (booking.value?.status === 'pending') {
    pollInterval = setInterval(fetchBooking, 1500)
  }
})
```

---

## Формат сообщения RabbitMQ

**Exchange:** `hotelpulse` (тип: `topic`, durable)
**Routing key:** `booking.created`
**Queue:** `bookings.created` (durable — переживает перезапуск брокера)

**Тело сообщения (JSON)**
```json
{
  "bookingId": "bkg_a3f9c12d8e4b",
  "hotelId":   "h_alpina",
  "roomId":    "r2",
  "createdAt": "2026-05-11T14:32:00.000Z"
}
```

**Свойства сообщения**
```
ContentType: application/json
Persistent:  true              ← сохраняется при перезапуске брокера
```

---

## Пайплайн обработки в Worker

При получении сообщения Worker имитирует 3-этапный пайплайн с реалистичными задержками:

| Этап | Задержка  | Действие                                          |
|------|-----------|---------------------------------------------------|
| 1    | +700 мс   | Отметить "message delivered" в event log          |
| 2    | +800 мс   | Отметить "reservation locked" в event log         |
| 3    | +900 мс   | Определить исход — 90% confirmed / 10% rejected   |

Итого: ~2.4 секунды от публикации сообщения до финального статуса.

При исходе `rejected` бронь получает:
```json
{
  "status": "rejected",
  "rejectionReason": "Room became unavailable during processing."
}
```

---

## Коллекции MongoDB

### `hotels`
Заполняется seed-данными при первом запуске API. Структура соответствует C#-рекорду `Hotel` в `apps/api/Models/Hotel.cs`.

### `bookings`
Создаётся через `POST /api/bookings`, обновляется Worker-ом. Ключевые поля:

| Поле               | Тип      | Кто устанавливает |
|--------------------|----------|-------------------|
| `_id`              | string   | API               |
| `status`           | string   | API / Worker      |
| `events`           | array    | API / Worker      |
| `confirmationCode` | string?  | Worker            |
| `rejectionReason`  | string?  | Worker            |
