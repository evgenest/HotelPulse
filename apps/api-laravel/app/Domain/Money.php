<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * Money is kept as a decimal string (Postgres numeric(10,2)) and compared with
 * bcmath, never as float, so 0.1 + 0.2 problems cannot reach a total.
 */
final class Money
{
    private const int Scale = 2;

    /** @return numeric-string|null */
    public static function normalize(mixed $value): ?string
    {
        if (is_int($value)) {
            return (string) $value;
        }
        if (is_float($value) && is_finite($value)) {
            return rtrim(rtrim(number_format($value, 10, '.', ''), '0'), '.');
        }
        if (is_string($value) && is_numeric($value)) {
            return $value;
        }

        return null;
    }

    /** @param numeric-string $a @param numeric-string $b */
    public static function equals(string $a, string $b): bool
    {
        return bccomp($a, $b, self::Scale) === 0;
    }

    /** @param numeric-string $price @return numeric-string */
    public static function times(string $price, int $count): string
    {
        return bcmul($price, (string) $count, self::Scale);
    }

    /** "390.00" → 390, "97.50" → 97.5: the same JSON number the .NET API sends. */
    public static function toJsonNumber(string $amount): int|float
    {
        $trimmed = str_contains($amount, '.') ? rtrim(rtrim($amount, '0'), '.') : $amount;

        return str_contains($trimmed, '.') ? (float) $trimmed : (int) $trimmed;
    }

    /** Price as .NET prints a numeric(10,2) decimal: "150.00". */
    public static function display(string $amount): string
    {
        return bcadd($amount, '0', self::Scale);
    }
}
