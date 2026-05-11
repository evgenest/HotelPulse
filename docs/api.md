# API Reference

## REST Endpoints

### Hotels

#### `GET /api/hotels`
Returns all hotels with their rooms.

**Response `200 OK`**
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
Returns a single hotel by ID.

**Response `200 OK`** — same shape as above (single object).
**Response `404`** — hotel not found.

---

### Bookings

#### `POST /api/bookings`
Creates a booking and publishes a message to RabbitMQ.

**The API does NOT process the booking synchronously.** It only writes a `pending` record to MongoDB and immediately publishes to the queue. The Worker picks it up within 2-3 seconds.

**Request body**
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

**Response `202 Accepted`**
```json
{ "id": "bkg_a3f9c12d8e4b", "status": "pending" }
```

#### `GET /api/bookings/{id}`
Returns the current state of a booking. Poll this endpoint to detect confirmation.

**Response `200 OK`**
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

**Status values:** `pending` · `confirmed` · `rejected`

**Response `404`** — booking not found.

#### `GET /health`
Liveness probe for Kubernetes.

**Response `200 OK`**
```json
{ "status": "ok", "time": "2026-05-11T14:32:00Z" }
```

---

## Polling Strategy

The frontend polls `GET /api/bookings/{id}` every **1500 ms** until `status !== "pending"`, then stops. No WebSockets or SSE — simple and sufficient for a demo.

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

## RabbitMQ Message Format

**Exchange:** `hotelpulse` (type: `topic`, durable)
**Routing key:** `booking.created`
**Queue:** `bookings.created` (durable, persisted across broker restarts)

**Message body (JSON)**
```json
{
  "bookingId": "bkg_a3f9c12d8e4b",
  "hotelId":   "h_alpina",
  "roomId":    "r2",
  "createdAt": "2026-05-11T14:32:00.000Z"
}
```

**Message properties**
```
ContentType: application/json
Persistent:  true              ← survives broker restart
```

---

## Worker Processing Pipeline

When the Worker receives a message it simulates a 3-step pipeline with realistic delays:

| Step | Delay    | Action                                   |
|------|----------|------------------------------------------|
| 1    | +700 ms  | Mark "message delivered" in event log    |
| 2    | +800 ms  | Mark "reservation locked" in event log   |
| 3    | +900 ms  | Resolve outcome — 90% confirmed / 10% rejected |

Total: ~2.4 seconds from message publish to terminal status.

On the "rejected" path, the booking gets:
```json
{
  "status": "rejected",
  "rejectionReason": "Room became unavailable during processing."
}
```

---

## MongoDB Collections

### `hotels`
Seeded on first API startup. Schema mirrors the `Hotel` C# record in `apps/api/Models/Hotel.cs`.

### `bookings`
Created by `POST /api/bookings`, updated by the Worker. Key fields:

| Field              | Type     | Set by   |
|--------------------|----------|----------|
| `_id`              | string   | API      |
| `status`           | string   | API / Worker |
| `events`           | array    | API / Worker |
| `confirmationCode` | string?  | Worker   |
| `rejectionReason`  | string?  | Worker   |
