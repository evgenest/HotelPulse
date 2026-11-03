<?php

declare(strict_types=1);

namespace App\Domain;

use DateTimeImmutable;
use DateTimeZone;

/** Same steps and timing as apps/HotelPulse.Domain/BookingTimeline.cs. */
final class BookingTimeline
{
    public const int Received = 0;

    public const int Published = 1;

    public const int Delivered = 2;

    public const int Locked = 3;

    /** @return list<BookingEvent> */
    public static function initial(DateTimeImmutable $now): array
    {
        return [
            new BookingEvent(BookingLabels::Received, true, self::format($now), false),
            new BookingEvent(BookingLabels::Published, false, null, true),
            new BookingEvent(BookingLabels::Delivered, false, null, false),
            new BookingEvent(BookingLabels::Locked, false, null, false),
        ];
    }

    /**
     * @param  list<BookingEvent>  $events
     * @return list<BookingEvent>
     */
    public static function advance(array $events, int $completedThrough, DateTimeImmutable $now): array
    {
        $next = $completedThrough + 1;
        $result = [];
        foreach ($events as $i => $event) {
            $result[] = match (true) {
                $i <= $completedThrough => $event->with(
                    done: true,
                    time: $event->time ?? self::format(self::plusMs($now, $i * 700)),
                    current: false,
                ),
                $i === $next => $event->with(done: false, current: true),
                default => $event->with(current: false),
            };
        }

        return $result;
    }

    /** @return list<BookingEvent> */
    public static function terminal(DateTimeImmutable $startedAt, DateTimeImmutable $completedAt, string $outcome): array
    {
        return [
            new BookingEvent(BookingLabels::Received, true, self::format($startedAt), false),
            new BookingEvent(BookingLabels::Published, true, self::format(self::plusMs($startedAt, 50)), false),
            new BookingEvent(BookingLabels::Delivered, true, self::format(self::plusMs($startedAt, 750)), false),
            new BookingEvent(BookingLabels::Locked, true, self::format(self::plusMs($startedAt, 1550)), false),
            new BookingEvent(BookingLabels::status($outcome), true, self::format($completedAt), false),
        ];
    }

    /**
     * What a cancel does to the timeline. The repository repeats this in SQL
     * against the stored value (ADR-0003); keep the two in step.
     *
     * @param  list<BookingEvent>  $events
     * @return list<BookingEvent>
     */
    public static function cancelled(array $events, DateTimeImmutable $now): array
    {
        $result = array_map(static fn (BookingEvent $e): BookingEvent => $e->with(current: false), $events);
        $result[] = self::cancelledEvent($now);

        return $result;
    }

    public static function cancelledEvent(DateTimeImmutable $now): BookingEvent
    {
        return new BookingEvent(BookingLabels::Cancelled, true, self::format($now), false);
    }

    public static function format(DateTimeImmutable $utc): string
    {
        return $utc->setTimezone(new DateTimeZone('UTC'))->format('H:i:s');
    }

    private static function plusMs(DateTimeImmutable $time, int $ms): DateTimeImmutable
    {
        return $time->modify("+{$ms} milliseconds");
    }
}
