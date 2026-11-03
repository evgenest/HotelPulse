<?php

declare(strict_types=1);

namespace App\Domain;

/** Same parsing as HotelPulseSettings.cs: default 0.9, clamped to [0, 1]. */
final class ConfirmProbability
{
    public const float Default = 0.9;

    public static function resolve(mixed $value): float
    {
        if (! is_scalar($value) || trim((string) $value) === '' || ! is_numeric(trim((string) $value))) {
            return self::Default;
        }

        return max(0.0, min(1.0, (float) trim((string) $value)));
    }
}
