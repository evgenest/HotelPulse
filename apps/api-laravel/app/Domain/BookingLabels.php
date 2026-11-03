<?php

declare(strict_types=1);

namespace App\Domain;

final class BookingLabels
{
    public const string Received = 'api → POST /bookings received';

    public const string Published = 'api → published to bookings.created';

    public const string Delivered = 'worker → message delivered';

    public const string Locked = 'worker → reservation locked in postgres';

    public const string Cancelled = 'api → booking cancelled';

    public static function status(string $outcome): string
    {
        return "worker → status: {$outcome}";
    }
}
