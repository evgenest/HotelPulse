<?php

declare(strict_types=1);

namespace App\Domain;

/** What the worker needs from storage. Implemented by BookingRepository. */
interface BookingStore
{
    public function find(string $id): ?Booking;

    /** @param list<BookingEvent> $events Written only while the booking is still pending. */
    public function updateEvents(string $id, array $events): void;

    /** Compare-and-set from pending. Returns false when the row was already terminal. */
    public function tryComplete(string $id, BookingCompletion $completion): bool;
}
