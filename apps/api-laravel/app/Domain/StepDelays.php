<?php

declare(strict_types=1);

namespace App\Domain;

/** Same parsing as apps/HotelPulse.Domain/StepDelays.cs. */
final class StepDelays
{
    public const array Default = [700, 800, 900];

    /** @return array{int, int, int} */
    public static function resolve(mixed $override): array
    {
        $value = is_scalar($override) ? trim((string) $override) : '';
        if ($value === '' || preg_match('/^\d+$/', $value) !== 1) {
            return self::Default;
        }

        $ms = (int) $value;

        return [$ms, $ms, $ms];
    }
}
