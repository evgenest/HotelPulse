<?php

declare(strict_types=1);

namespace App\Infrastructure\Postgres;

use App\Domain\Booking;
use App\Domain\BookingCompletion;
use App\Domain\BookingEvent;
use App\Domain\BookingMessage;
use App\Domain\BookingStore;
use App\Domain\IdempotencyConflictException;
use App\Domain\LocalDate;
use App\Domain\RoomUnavailableException;
use DateTimeImmutable;
use DateTimeZone;
use Illuminate\Database\QueryException;
use Illuminate\Support\Facades\DB;
use Throwable;

/**
 * Bookings in Postgres. Every rule that concurrent requests could break is
 * enforced in SQL (ADR-0001): the exclusion constraint, compare-and-set
 * updates and primary keys. Same SQL as the .NET BookingRepository.
 */
final class BookingRepository implements BookingStore
{
    public const string ExclusionViolation = '23P01';

    public const string UniqueViolation = '23505';

    public const string DeadlockDetected = '40P01';

    private const int MaxCreateAttempts = 3;

    private const string Columns = <<<'SQL'
        id, hotel_id, hotel_name, room_id, room_type, guest_name, check_in, check_out,
        nights, total::text AS total, status, confirmation_code, rejection_reason,
        created_at, cancelled_at, events::text AS events
        SQL;

    public function find(string $id): ?Booking
    {
        $row = DB::selectOne('SELECT '.self::Columns.' FROM bookings WHERE id = :id', ['id' => $id]);

        return $row === null ? null : self::map($row);
    }

    public function updateEvents(string $id, array $events): void
    {
        DB::update(
            "UPDATE bookings SET events = CAST(:events AS jsonb) WHERE id = :id AND status = 'pending'",
            ['id' => $id, 'events' => self::json($events)],
        );
    }

    public function tryComplete(string $id, BookingCompletion $completion): bool
    {
        $updated = DB::update(<<<'SQL'
            UPDATE bookings
            SET status = :status,
                confirmation_code = :confirmation_code,
                rejection_reason = :rejection_reason,
                events = CAST(:events AS jsonb)
            WHERE id = :id
              AND status = 'pending'
            SQL, [
            'id' => $id,
            'status' => $completion->status,
            'confirmation_code' => $completion->confirmationCode,
            'rejection_reason' => $completion->rejectionReason,
            'events' => self::json($completion->events),
        ]);

        return $updated === 1;
    }

    /**
     * Inserts the booking, its outbox message and its idempotency key in one
     * transaction. Returns [booking, replay].
     *
     * @return array{Booking, bool}
     */
    public function create(Booking $booking, ?string $idempotencyKey, string $requestHash): array
    {
        // Parallel inserts that conflict on the exclusion constraint can still
        // deadlock in rare orders; the aborted transaction changed nothing, so
        // running it again is safe (ADR-0005).
        for ($attempt = 1; ; $attempt++) {
            try {
                return $this->createOnce($booking, $idempotencyKey, $requestHash);
            } catch (QueryException $e) {
                if (self::sqlState($e) !== self::DeadlockDetected || $attempt >= self::MaxCreateAttempts) {
                    throw $e;
                }
                usleep(random_int(5, 25) * 1000 * $attempt);
            }
        }
    }

    /** @return list<Booking> */
    public function listRecent(int $limit): array
    {
        $rows = DB::select('SELECT '.self::Columns.' FROM bookings ORDER BY created_at DESC LIMIT :limit', ['limit' => $limit]);

        return array_values(array_map(self::map(...), $rows));
    }

    /**
     * @param  list<string>  $ids
     * @return list<Booking>
     */
    public function getMany(array $ids): array
    {
        if ($ids === []) {
            return [];
        }

        $rows = DB::select(
            'SELECT '.self::Columns.' FROM bookings WHERE id = ANY(CAST(:ids AS text[]))',
            ['ids' => self::textArray($ids)],
        );
        $byId = [];
        foreach ($rows as $row) {
            $booking = self::map($row);
            $byId[$booking->id] = $booking;
        }

        return array_values(array_filter(array_map(static fn (string $id): ?Booking => $byId[$id] ?? null, $ids)));
    }

    /**
     * Cancels if the booking is still pending or confirmed and check-in is
     * after $today; null when the row no longer matches. The cancel event is
     * appended to the stored timeline inside the UPDATE, so worker steps
     * written after the caller read the row are kept (ADR-0003).
     */
    public function tryCancel(string $id, LocalDate $today, BookingEvent $cancelEvent): ?Booking
    {
        $row = DB::selectOne(<<<'SQL'
            UPDATE bookings
            SET status = 'cancelled',
                cancelled_at = now(),
                events = (
                    SELECT coalesce(jsonb_agg(e || '{"current": false}'::jsonb ORDER BY ord), '[]'::jsonb)
                    FROM jsonb_array_elements(bookings.events) WITH ORDINALITY AS t(e, ord)
                ) || jsonb_build_array(CAST(:cancel_event AS jsonb))
            WHERE id = :id
              AND status IN ('pending', 'confirmed')
              AND check_in > :today
            RETURNING
            SQL.' '.self::Columns, [
            'id' => $id,
            'today' => (string) $today,
            'cancel_event' => json_encode($cancelEvent, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE),
        ]);

        return $row === null ? null : self::map($row);
    }

    /** @return array{Booking, bool} */
    private function createOnce(Booking $booking, ?string $idempotencyKey, string $requestHash): array
    {
        if ($idempotencyKey !== null && ($existing = $this->findReplay($idempotencyKey, $requestHash)) !== null) {
            return [$existing, true];
        }

        DB::beginTransaction();
        try {
            // Queue inserts for the same room behind one transaction-scoped
            // lock (ADR-0005). The constraint still forbids the overlap.
            DB::select('SELECT pg_advisory_xact_lock(hashtextextended(:room_key, 0))', [
                'room_key' => $booking->hotelId.'/'.$booking->roomId,
            ]);

            if ($idempotencyKey !== null && ($existing = $this->findReplay($idempotencyKey, $requestHash)) !== null) {
                DB::commit();

                return [$existing, true];
            }

            DB::insert(<<<'SQL'
                INSERT INTO bookings (
                    id, hotel_id, room_id, hotel_name, room_type, guest_name,
                    check_in, check_out, nights, total, status,
                    confirmation_code, rejection_reason, created_at, events)
                VALUES (
                    :id, :hotel_id, :room_id, :hotel_name, :room_type, :guest_name,
                    :check_in, :check_out, :nights, :total, :status,
                    NULL, NULL, :created_at, CAST(:events AS jsonb))
                SQL, [
                'id' => $booking->id,
                'hotel_id' => $booking->hotelId,
                'room_id' => $booking->roomId,
                'hotel_name' => $booking->hotelName,
                'room_type' => $booking->roomType,
                'guest_name' => $booking->guestName,
                'check_in' => (string) $booking->checkIn,
                'check_out' => (string) $booking->checkOut,
                'nights' => $booking->nights,
                'total' => $booking->total,
                'status' => $booking->status,
                'created_at' => $booking->createdAt->format('Y-m-d H:i:s.uP'),
                'events' => self::json($booking->events),
            ]);

            $message = new BookingMessage($booking->id, $booking->hotelId, $booking->roomId, $booking->createdAt);
            DB::insert('INSERT INTO outbox (booking_id, payload) VALUES (:booking_id, CAST(:payload AS jsonb))', [
                'booking_id' => $booking->id,
                'payload' => json_encode($message, JSON_THROW_ON_ERROR),
            ]);

            if ($idempotencyKey !== null) {
                DB::insert(
                    'INSERT INTO idempotency_keys (idempotency_key, request_hash, booking_id) VALUES (:key, :hash, :booking_id)',
                    ['key' => $idempotencyKey, 'hash' => $requestHash, 'booking_id' => $booking->id],
                );
            }

            DB::commit();

            return [$booking, false];
        } catch (QueryException $e) {
            DB::rollBack();
            $state = self::sqlState($e);

            if ($state === self::ExclusionViolation) {
                // A parallel request with the same key trips the exclusion
                // constraint before our key insert; the other transaction has
                // committed by now, so its key is visible (ADR-0004).
                if ($idempotencyKey !== null && ($existing = $this->findReplay($idempotencyKey, $requestHash)) !== null) {
                    return [$existing, true];
                }
                throw new RoomUnavailableException;
            }

            if ($state === self::UniqueViolation && $idempotencyKey !== null && str_contains($e->getMessage(), 'idempotency_keys_pkey')) {
                $existing = $this->findReplay($idempotencyKey, $requestHash);
                if ($existing !== null) {
                    return [$existing, true];
                }
            }

            throw $e;
        } catch (Throwable $e) {
            if (DB::transactionLevel() > 0) {
                DB::rollBack();
            }
            throw $e;
        }
    }

    private function findReplay(string $idempotencyKey, string $requestHash): ?Booking
    {
        $stored = DB::selectOne(
            'SELECT request_hash, booking_id FROM idempotency_keys WHERE idempotency_key = :key',
            ['key' => $idempotencyKey],
        );
        if ($stored === null) {
            return null;
        }
        if (! hash_equals(Row::str($stored, 'request_hash'), $requestHash)) {
            throw new IdempotencyConflictException;
        }

        return $this->find(Row::str($stored, 'booking_id'));
    }

    public static function sqlState(QueryException $e): string
    {
        $info = $e->errorInfo;

        return is_array($info) && isset($info[0]) ? (string) $info[0] : (string) $e->getCode();
    }

    /** @param list<BookingEvent> $events */
    private static function json(array $events): string
    {
        return json_encode($events, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
    }

    /** @param list<string> $values */
    private static function textArray(array $values): string
    {
        return '{'.implode(',', array_map(
            static fn (string $v): string => '"'.str_replace(['\\', '"'], ['\\\\', '\\"'], $v).'"',
            $values,
        )).'}';
    }

    private static function map(object $row): Booking
    {
        /** @var list<array{label: string, done: bool, time?: ?string, current: bool}> $events */
        $events = json_decode(Row::str($row, 'events') ?: '[]', true, 8, JSON_THROW_ON_ERROR);
        $utc = new DateTimeZone('UTC');
        $cancelledAt = Row::strOrNull($row, 'cancelled_at');

        return new Booking(
            Row::str($row, 'id'),
            Row::str($row, 'hotel_id'),
            Row::str($row, 'hotel_name'),
            Row::str($row, 'room_id'),
            Row::str($row, 'room_type'),
            Row::str($row, 'guest_name'),
            LocalDate::parse(Row::str($row, 'check_in')),
            LocalDate::parse(Row::str($row, 'check_out')),
            Row::int($row, 'nights'),
            Row::numeric($row, 'total'),
            Row::str($row, 'status'),
            Row::strOrNull($row, 'confirmation_code'),
            Row::strOrNull($row, 'rejection_reason'),
            (new DateTimeImmutable(Row::str($row, 'created_at')))->setTimezone($utc),
            array_map(BookingEvent::fromArray(...), $events),
            $cancelledAt === null ? null : (new DateTimeImmutable($cancelledAt))->setTimezone($utc),
        );
    }
}
