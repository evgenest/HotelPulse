# Spec: booking cancellation

Status: implemented in v1.0.0
Owner: Evgenii Churin

## Problem

A guest can create a booking but cannot take it back. A room that the guest no
longer needs stays held until the stay is over, so nobody else can book it.

## Goal

A guest can cancel a booking before check-in. The room becomes available for
those dates right away, in the database and in every cached read.

## Out of scope

- Refunds and cancellation fees. HotelPulse has no payments.
- Authentication. Anyone who knows the booking id can cancel it, the same way
  anyone who knows the id can read it today.
- Cancelling through the queue. Cancellation is a synchronous API write.

## Rules

1. A booking can be cancelled while its status is `pending` or `confirmed`.
2. A booking can be cancelled only before its check-in date. The cut-off is
   the check-in day itself, in UTC: on the check-in day the request is refused.
3. A `rejected` booking cannot be cancelled. It never held the room.
4. Cancelling an already `cancelled` booking succeeds and changes nothing.
   Retries from a flaky network must be safe.
5. A cancelled booking no longer holds the room. A new booking for the same
   room and dates is accepted.
6. If the worker is still processing a `pending` booking when it is cancelled,
   the cancellation wins. The worker's compare-and-set from `pending` finds no
   row to update and acknowledges the message.

## Status model

```
pending ──► confirmed ──► cancelled
   │                         ▲
   ├────────────────────────►┘
   └──► rejected
```

`cancelled` is terminal. `confirmed` and `rejected` stay terminal for the
worker: it never moves a booking out of them.

## API

`POST /api/bookings/{id}/cancel`

No request body.

| Case | Response |
|---|---|
| Booking was `pending` or `confirmed`, check-in is in the future | `200 OK` with the updated booking |
| Booking is already `cancelled` | `200 OK` with the booking, unchanged |
| Booking does not exist | `404 Not Found` |
| Booking is `rejected` | `409 Conflict`, `{ "error": "A rejected booking cannot be cancelled." }` |
| Check-in is today or in the past | `409 Conflict`, `{ "error": "Bookings can only be cancelled before the check-in date." }` |

The returned booking has `status: "cancelled"`, a `cancelledAt` timestamp, and
one more timeline event: `api → booking cancelled`. A booking that was
confirmed keeps its confirmation code, so the guest can still refer to it.

## Data

Migration `003_cancellation.sql`:

- adds `cancelled_at timestamptz NULL` to `bookings`;
- allows `cancelled` in `bookings_status_chk`;
- extends `bookings_confirmation_chk`: a cancelled row has a `cancelled_at`,
  no rejection reason, and an optional confirmation code;
- requires `cancelled_at` to be set exactly when the status is `cancelled`.

The exclusion constraint `bookings_no_overlapping_stay` and the partial index
`bookings_availability_idx` already filter on `status IN ('pending', 'confirmed')`,
so a cancelled row releases the room without any index change.

The update is one statement with the rules in its `WHERE` clause, so two
concurrent cancel requests, or a cancel racing the worker, cannot both win:

```sql
UPDATE bookings
SET status = 'cancelled', cancelled_at = now(), events = @events::jsonb
WHERE id = @id
  AND status IN ('pending', 'confirmed')
  AND check_in > @today
```

## Cache

After a successful cancel, the API calls `InvalidateHotelAsync(hotelId)`, the
same version bump a new booking uses. The next availability and catalog reads
miss and load PostgreSQL.

## Frontend

The booking page shows a **Cancel booking** button while the booking is
`pending` or `confirmed` and check-in is in the future. The button asks for
confirmation. After a successful cancel the page shows the cancelled state.
The history rail and status pills show `cancelled` in a neutral colour.

## Tests

Unit tests (`BookingCancellationTests`):

- each status and date combination returns the decision in the rules table;
- `cancelled` is terminal and does not hold inventory;
- the worker does not treat `cancelled` as a legal target.

Integration tests (`CancellationTests`), against real Postgres, Redis and
RabbitMQ:

- confirm, cancel, get `200` and `cancelled`;
- after cancel, availability for those dates shows the room free and a new
  booking for the same room and dates returns `202`;
- a second cancel returns `200` and the same `cancelledAt`;
- an unknown id returns `404`;
- cancelling a booking whose check-in is today returns `409`.
