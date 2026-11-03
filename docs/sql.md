# SQL: availability and the recent-bookings list

PostgreSQL is the system of record for hotels, rooms, and bookings. Two things in `apps/HotelPulse.Infrastructure/Migrations/001_init.sql` are doing different jobs:

- `bookings_no_overlapping_stay` is an exclusion constraint. It is the write path. A btree `UNIQUE` index cannot express "these two date ranges overlap", so the constraint uses GiST (`btree_gist` for equality on `hotel_id` / `room_id`, plus `daterange(..., '[)')` for the stay). Pending and confirmed rows hold the room. A rejected row drops out of the predicate, which releases the dates. Checkout morning does not collide with the next check-in.
- `bookings_availability_idx` is a partial btree on `(hotel_id, room_id, check_in, check_out) WHERE status IN ('pending', 'confirmed')`. It is the read path. The availability query is written as inequalities so the planner can satisfy the whole predicate from that index.

The query lives in `AvailabilitySql.Query`:

```sql
SELECT r.id, r.type, r.capacity, r.price, r.sqm,
       NOT EXISTS (
           SELECT 1
           FROM bookings b
           WHERE b.hotel_id = r.hotel_id
             AND b.room_id = r.id
             AND b.status IN ('pending', 'confirmed')
             AND b.check_in < @checkOut
             AND b.check_out > @checkIn
       ) AS available
FROM rooms r
WHERE r.hotel_id = @hotelId
ORDER BY r.price, r.id;
```

`check_in < checkout AND check_out > checkin` is the same half-open overlap as `daterange(check_in, check_out, '[)') && daterange(...)`. The code twins are `StayOverlap.Overlaps` in C# and `StayOverlap::overlaps` in PHP; both are unit-tested against the same cases.

## Measured plan

Against PostgreSQL 18 with the six hotels, 19 rooms, and 20,000 confirmed one-night stays on The Porter's `r1` (`ANALYZE` first). The plan below was captured on PostgreSQL 17; the same indexes serve the query on 18:

```text
Sort  (cost=8.53..8.54 rows=4 width=9) (actual time=0.080..0.080 rows=4 loops=1)
  Sort Key: r.price, r.id
  ->  Seq Scan on rooms r  (cost=0.00..8.49 rows=4 width=9) (actual time=0.035..0.043 rows=4 loops=1)
        Filter: (hotel_id = 'h_porter'::text)
        Rows Removed by Filter: 15
        SubPlan 1
          ->  Index Only Scan using bookings_availability_idx on bookings b
               (cost=0.29..250.34 rows=164 width=0) (actual time=0.009..0.009 rows=0 loops=4)
                Index Cond: ((hotel_id = r.hotel_id) AND (room_id = r.id)
                         AND (check_in < '2030-06-15'::date)
                         AND (check_out > '2030-06-12'::date))
                Heap Fetches: 1
Execution Time: 0.118 ms
```

What this says:

- `rooms` is 19 rows. A sequential scan plus a filter is the right plan. An index on hotels would not change it.
- For each of the four rooms the subquery is an **index-only scan** of `bookings_availability_idx`. The index condition includes both inequalities, so a stay that starts before checkout and ends after check-in is found without scanning the 20,000-row table.
- The same predicate on room `r1` matches 3 stored stays. The plan prints `rows=0` because that figure is the average over the four loops, and the other three room types have no rows in the window. `Heap Fetches: 1` is the visibility check for the one loop that hit.

`GET /api/bookings` (no `ids`) is the other indexed read. With the same 20,000 rows:

```text
Limit  (cost=0.29..1.05 rows=20 width=22) (actual time=0.016..0.020 rows=20 loops=1)
  ->  Index Scan using bookings_recent_idx on bookings
        (cost=0.29..760.29 rows=20000 width=22) (actual time=0.015..0.018 rows=20 loops=1)
Execution Time: 0.038 ms
```

`bookings_recent_idx` is `(created_at DESC)`. The planner walks the index and stops after 20 rows. It does not sort the table.

Hotel list and hotel detail are one statement each: hotels left-joined to a `LATERAL jsonb_agg` of their rooms, ordered by price. Six hotels, so there is no per-hotel round trip.

## Cache invalidation

Redis is not in front of these plans as a second database. `HotelCatalog` caches the JSON of three reads:

| Read | Key |
|---|---|
| `GET /api/hotels` | `hp:hotels:v{catalogVersion}` |
| `GET /api/hotels/{id}` | `hp:hotels:{id}:v{hotelVersion}` |
| `GET /api/hotels/{id}/availability` | `hp:avail:{id}:v{hotelVersion}:{checkIn}:{checkOut}` |

Creating a booking, or the worker moving one to confirmed or rejected, increments `hp:ver:catalog` and `hp:ver:hotel:{id}`. The next read uses a new key, so it cannot return the previous payload. Entries also expire after 60 seconds. A Redis outage is a cache bypass: the handler reads PostgreSQL and sends `X-Cache: BYPASS`. `HIT` and `MISS` are set the same way when Redis answers.
