-- Booking cancellation. See docs/specs/booking-cancellation.md.
--
-- The exclusion constraint and the availability index already filter on
-- status IN ('pending', 'confirmed'), so a cancelled row stops holding the
-- room without any index change.

ALTER TABLE bookings ADD COLUMN cancelled_at timestamptz;

ALTER TABLE bookings DROP CONSTRAINT bookings_status_chk;
ALTER TABLE bookings ADD CONSTRAINT bookings_status_chk
    CHECK (status IN ('pending', 'confirmed', 'rejected', 'cancelled'));

ALTER TABLE bookings DROP CONSTRAINT bookings_confirmation_chk;
ALTER TABLE bookings ADD CONSTRAINT bookings_confirmation_chk CHECK (
    (status = 'confirmed' AND confirmation_code IS NOT NULL AND rejection_reason IS NULL)
    OR (status = 'rejected' AND confirmation_code IS NULL AND rejection_reason IS NOT NULL)
    OR (status = 'pending' AND confirmation_code IS NULL AND rejection_reason IS NULL)
    -- A cancelled booking keeps the confirmation code it had, if any.
    OR (status = 'cancelled' AND rejection_reason IS NULL)
);

ALTER TABLE bookings ADD CONSTRAINT bookings_cancelled_at_chk
    CHECK ((status = 'cancelled') = (cancelled_at IS NOT NULL));
