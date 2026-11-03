-- Checks after a load run (docs/specs/laravel-port.md → «Нагрузочная проверка»).
-- Both numbers must be 0.

-- Two active stays on one room with overlapping dates. The exclusion
-- constraint forbids this; the query proves it held under load.
SELECT count(*) AS overlapping_active_pairs
FROM bookings a
JOIN bookings b
  ON a.hotel_id = b.hotel_id
 AND a.room_id = b.room_id
 AND a.id < b.id
 AND daterange(a.check_in, a.check_out, '[)') && daterange(b.check_in, b.check_out, '[)')
WHERE a.status IN ('pending', 'confirmed')
  AND b.status IN ('pending', 'confirmed');

-- Bookings still pending (run after the worker has had time to drain the queue).
SELECT count(*) AS still_pending FROM bookings WHERE status = 'pending';
