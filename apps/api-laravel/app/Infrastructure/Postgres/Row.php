<?php

declare(strict_types=1);

namespace App\Infrastructure\Postgres;

use UnexpectedValueException;

/**
 * Typed reads from a database row (a stdClass from DB::select). Each read
 * checks the type, so a schema that drifted from the code fails here, at the
 * column, instead of somewhere deep in the domain.
 */
final class Row
{
    public static function str(object $row, string $column): string
    {
        $value = self::value($row, $column);
        if (! is_string($value)) {
            throw self::wrongType($column, 'string', $value);
        }

        return $value;
    }

    public static function strOrNull(object $row, string $column): ?string
    {
        $value = self::value($row, $column);

        return $value === null ? null : self::str($row, $column);
    }

    public static function int(object $row, string $column): int
    {
        $value = self::value($row, $column);
        if (is_int($value)) {
            return $value;
        }
        if (is_string($value) && preg_match('/^-?\d+$/', $value) === 1) {
            return (int) $value;
        }

        throw self::wrongType($column, 'int', $value);
    }

    public static function bool(object $row, string $column): bool
    {
        $value = self::value($row, $column);
        if (! is_bool($value)) {
            throw self::wrongType($column, 'bool', $value);
        }

        return $value;
    }

    /** @return numeric-string */
    public static function numeric(object $row, string $column): string
    {
        $value = self::str($row, $column);
        if (! is_numeric($value)) {
            throw self::wrongType($column, 'numeric string', $value);
        }

        return $value;
    }

    private static function value(object $row, string $column): mixed
    {
        if (! property_exists($row, $column)) {
            throw new UnexpectedValueException("Column {$column} is missing from the row.");
        }

        return $row->{$column};
    }

    private static function wrongType(string $column, string $expected, mixed $value): UnexpectedValueException
    {
        return new UnexpectedValueException("Column {$column} should be {$expected}, got ".get_debug_type($value).'.');
    }
}
