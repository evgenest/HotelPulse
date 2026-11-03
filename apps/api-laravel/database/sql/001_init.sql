-- Hotels, rooms, and bookings are relational: a room belongs to one hotel,
-- and two active stays for the same room must not overlap. PostgreSQL is the
-- system of record. Redis only caches reads.

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE hotels (
    id text PRIMARY KEY,
    name text NOT NULL,
    city text NOT NULL,
    rating smallint NOT NULL CHECK (rating BETWEEN 1 AND 5),
    glyph text NOT NULL,
    price_from numeric(10, 2) NOT NULL CHECK (price_from >= 0),
    description text NOT NULL,
    amenities text[] NOT NULL
);

CREATE TABLE rooms (
    hotel_id text NOT NULL REFERENCES hotels (id),
    id text NOT NULL,
    type text NOT NULL,
    capacity integer NOT NULL CHECK (capacity > 0),
    price numeric(10, 2) NOT NULL CHECK (price >= 0),
    sqm integer NOT NULL CHECK (sqm > 0),
    PRIMARY KEY (hotel_id, id)
);

CREATE TABLE bookings (
    id text PRIMARY KEY,
    hotel_id text NOT NULL,
    room_id text NOT NULL,
    hotel_name text NOT NULL,
    room_type text NOT NULL,
    guest_name text NOT NULL,
    check_in date NOT NULL,
    check_out date NOT NULL,
    nights integer NOT NULL,
    total numeric(10, 2) NOT NULL CHECK (total >= 0),
    status text NOT NULL,
    confirmation_code text,
    rejection_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    events jsonb NOT NULL,
    CONSTRAINT bookings_room_fk FOREIGN KEY (hotel_id, room_id) REFERENCES rooms (hotel_id, id),
    CONSTRAINT bookings_stay_positive CHECK (check_out > check_in),
    CONSTRAINT bookings_nights_match CHECK (nights = (check_out - check_in)),
    CONSTRAINT bookings_guest_name_len CHECK (char_length(btrim(guest_name)) BETWEEN 1 AND 120),
    CONSTRAINT bookings_status_chk CHECK (status IN ('pending', 'confirmed', 'rejected')),
    CONSTRAINT bookings_confirmation_chk CHECK (
        (status = 'confirmed' AND confirmation_code IS NOT NULL AND rejection_reason IS NULL)
        OR (status = 'rejected' AND confirmation_code IS NULL AND rejection_reason IS NOT NULL)
        OR (status = 'pending' AND confirmation_code IS NULL AND rejection_reason IS NULL)
    ),
    -- Half-open ranges: checkout morning is free for the next guest.
    -- Rejected rows are excluded, so a rejection releases the room.
    -- btree_gist supplies the equality operator for text inside a GiST index;
    -- daterange overlap is what a plain UNIQUE btree cannot express.
    CONSTRAINT bookings_no_overlapping_stay
        EXCLUDE USING gist (
            hotel_id WITH =,
            room_id WITH =,
            daterange(check_in, check_out, '[)') WITH &&
        )
        WHERE (status IN ('pending', 'confirmed'))
);

-- History rail: newest 20 bookings.
CREATE INDEX bookings_recent_idx ON bookings (created_at DESC);

-- Availability reads. Equality on (hotel_id, room_id) plus check_in < checkout
-- is a range scan; check_out > check_in is applied as a residual filter.
-- The GiST constraint above is the concurrency guarantee. This partial btree
-- is the read path, and it ignores rejected rows that no longer hold a room.
CREATE INDEX bookings_availability_idx
    ON bookings (hotel_id, room_id, check_in, check_out)
    WHERE status IN ('pending', 'confirmed');

CREATE TABLE outbox (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id text NOT NULL REFERENCES bookings (id),
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz
);

CREATE INDEX outbox_unpublished_idx ON outbox (id) WHERE published_at IS NULL;

CREATE TABLE idempotency_keys (
    idempotency_key text PRIMARY KEY,
    request_hash text NOT NULL,
    booking_id text NOT NULL REFERENCES bookings (id),
    created_at timestamptz NOT NULL DEFAULT now()
);
