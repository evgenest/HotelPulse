<?php

declare(strict_types=1);

namespace App\Domain;

use DateTimeImmutable;
use DateTimeZone;

final class Clock
{
    public static function nowUtc(): DateTimeImmutable
    {
        return new DateTimeImmutable('now', new DateTimeZone('UTC'));
    }

    /** ISO 8601 in UTC with a Z suffix, like System.Text.Json writes a UTC DateTime. */
    public static function toJson(DateTimeImmutable $time): string
    {
        $utc = $time->setTimezone(new DateTimeZone('UTC'));
        $micro = rtrim($utc->format('u'), '0');

        return $utc->format('Y-m-d\TH:i:s').($micro === '' ? '' : '.'.$micro).'Z';
    }
}
