<?php

declare(strict_types=1);

namespace App\Infrastructure\Postgres;

use App\Domain\Hotel;
use App\Domain\LocalDate;
use App\Domain\Room;
use App\Domain\RoomAvailability;
use Illuminate\Support\Facades\DB;

/** Same SQL as apps/HotelPulse.Infrastructure/Postgres/HotelRepository.cs. */
final class HotelRepository
{
    private const string HotelSql = <<<'SQL'
        SELECT h.id,
               h.name,
               h.city,
               h.rating,
               h.glyph,
               h.price_from,
               h.description,
               array_to_json(h.amenities)::text AS amenities,
               COALESCE(rooms.rooms, '[]'::jsonb)::text AS rooms
        FROM hotels h
        LEFT JOIN LATERAL (
            SELECT jsonb_agg(
                       jsonb_build_object(
                           'id', room.id,
                           'type', room.type,
                           'capacity', room.capacity,
                           'price', room.price::text,
                           'sqm', room.sqm
                       )
                       ORDER BY room.price, room.id
                   ) AS rooms
            FROM rooms room
            WHERE room.hotel_id = h.id
        ) rooms ON true
        SQL;

    /**
     * One indexed lookup per room. Same predicate as AvailabilitySql.cs: only
     * pending and confirmed hold a room, ranges are half-open.
     */
    private const string AvailabilitySql = <<<'SQL'
        SELECT r.id,
               r.type,
               r.capacity,
               r.price::text AS price,
               r.sqm,
               NOT EXISTS (
                   SELECT 1
                   FROM bookings b
                   WHERE b.hotel_id = r.hotel_id
                     AND b.room_id = r.id
                     AND b.status IN ('pending', 'confirmed')
                     AND b.check_in < :check_out
                     AND b.check_out > :check_in
               ) AS available
        FROM rooms r
        WHERE r.hotel_id = :hotel_id
        ORDER BY r.price, r.id
        SQL;

    /** @return list<Hotel> */
    public function list(): array
    {
        return array_values(array_map(self::map(...), DB::select(self::HotelSql.' ORDER BY h.name')));
    }

    public function get(string $id): ?Hotel
    {
        $row = DB::selectOne(self::HotelSql.' WHERE h.id = :id', ['id' => $id]);

        return $row === null ? null : self::map($row);
    }

    /** @return list<RoomAvailability> */
    public function availability(string $hotelId, LocalDate $checkIn, LocalDate $checkOut): array
    {
        $rows = DB::select(self::AvailabilitySql, [
            'hotel_id' => $hotelId,
            'check_in' => (string) $checkIn,
            'check_out' => (string) $checkOut,
        ]);

        return array_values(array_map(static fn (object $r): RoomAvailability => new RoomAvailability(
            Row::str($r, 'id'),
            Row::str($r, 'type'),
            Row::int($r, 'capacity'),
            Row::numeric($r, 'price'),
            Row::int($r, 'sqm'),
            Row::bool($r, 'available'),
        ), $rows));
    }

    private static function map(object $row): Hotel
    {
        /** @var list<object> $rooms */
        $rooms = json_decode(Row::str($row, 'rooms'), false, 8, JSON_THROW_ON_ERROR);
        /** @var list<string> $amenities */
        $amenities = json_decode(Row::strOrNull($row, 'amenities') ?? '[]', true, 4, JSON_THROW_ON_ERROR);

        return new Hotel(
            Row::str($row, 'id'),
            Row::str($row, 'name'),
            Row::str($row, 'city'),
            Row::int($row, 'rating'),
            Row::str($row, 'glyph'),
            Row::numeric($row, 'price_from'),
            Row::str($row, 'description'),
            $amenities,
            array_map(static fn (object $r): Room => new Room(
                Row::str($r, 'id'),
                Row::str($r, 'type'),
                Row::int($r, 'capacity'),
                Row::numeric($r, 'price'),
                Row::int($r, 'sqm'),
            ), $rooms),
        );
    }
}
