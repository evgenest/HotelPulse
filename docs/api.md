# API

Base URL in development: `http://localhost:8080`.

Dates are `yyyy-MM-dd`. Money is a JSON number. `createdAt` is UTC.

## Hotels

### `GET /api/hotels`

The six hotels and their rooms, ordered by name. Cached. Header `X-Cache` is `HIT`, `MISS`, or `BYPASS`.

### `GET /api/hotels/{id}`

One hotel. `404` if the id is unknown. Same cache header.

### `GET /api/hotels/{id}/availability?checkIn=2027-04-01&checkOut=2027-04-03`

Each room with `available: true` when no pending or confirmed stay overlaps that half-open window. `404` if the hotel does not exist. `400` if the dates are missing or checkout is not after check-in. Cached per hotel and date pair; a booking for that hotel drops the cache.

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

The SQL and its plan are in [`sql.md`](sql.md).

## Bookings

### `POST /api/bookings`

Does not confirm the stay. It validates the body, inserts `pending`, enqueues an outbox message, and returns **202 Accepted**.

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

`nights` must equal the number of nights between the dates. `total` must equal the room price times that number. The guest name is required and at most 120 characters. Check-in cannot be before today (UTC).

**202**

```json
{ "id": "bkg_a3f9c12d8e4b", "status": "pending" }
```

Optional header `Idempotency-Key` (max 200 characters). The same key and the same body return the original booking. The same key with a different body is **409**.

**400** — validation errors: `{ "errors": [{ "field": "total", "message": "..." }] }`.

**404** — unknown hotel or room.

**409** — those dates are already held by a pending or confirmed stay on that room.

### `GET /api/bookings`

The 20 most recent bookings, newest first.

### `GET /api/bookings?ids={id1},{id2}`

Up to 20 bookings, in the requested order.

### `GET /api/bookings/{id}`

Poll this until `status` is `confirmed` or `rejected`.

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

A rejection sets `rejectionReason` and leaves `confirmationCode` null. About one booking in ten is rejected unless `HOTELPULSE_CONFIRM_PROBABILITY=1`.

### `POST /api/bookings/{id}/cancel`

Cancels a `pending` or `confirmed` booking before its check-in date. No body. The full rules are in [`specs/booking-cancellation.md`](specs/booking-cancellation.md).

| Case | Response |
|---|---|
| Cancelled now | `200` with the booking: `status: "cancelled"`, `cancelledAt`, and the event `api → booking cancelled` |
| Already cancelled | `200` with the booking, unchanged |
| Unknown id | `404` |
| Booking is `rejected` | `409` |
| Check-in is today or earlier (UTC) | `409` |

A cancelled booking no longer holds the room, and the hotel's cached reads are invalidated.

### `GET /health`

`200` when Postgres answers `SELECT 1`. `503` otherwise. Redis is not required for readiness. The body is `{ status, version, time }`.

## Queue

Exchange `hotelpulse` (topic, durable). Routing key `booking.created`. Queue `bookings.created` (durable). Messages are persistent.

```json
{
  "bookingId": "bkg_a3f9c12d8e4b",
  "hotelId": "h_porter",
  "roomId": "r2",
  "createdAt": "2026-11-02T14:32:00Z"
}
```

The row is committed before the publish. If the broker is down, the outbox relay retries. The worker acks only after the booking reaches a terminal status. A crash mid-pipeline redelivers; the status update is a compare-and-set from `pending`, so a second run does not mint a second confirmation code. A poison payload is discarded. A transient error is requeued once and then the booking is rejected. A database outage is requeued even on the second delivery, so the message is not dropped while Postgres is unreachable.

The pipeline waits 700 ms, then 800 ms, then 900 ms unless `HOTELPULSE_STEP_DELAY_MS` is set.

## Tables

`hotels`, `rooms`, `bookings`, `outbox`, `idempotency_keys`, plus `schema_migrations`. Created by `apps/HotelPulse.Infrastructure/Migrations`. The catalog is `002_seed.sql`.
