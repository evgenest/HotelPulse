<?php

declare(strict_types=1);

namespace App\Domain;

use DateTimeImmutable;
use DateTimeZone;
use Stringable;

/**
 * A calendar date without time or zone (DateOnly in .NET). Parsed strictly
 * as yyyy-MM-dd, so "2027-02-30" and "2027-4-1" are rejected.
 */
final readonly class LocalDate implements Stringable
{
    private function __construct(public int $dayNumber) {}

    public static function tryParse(?string $value): ?self
    {
        if ($value === null || preg_match('/^\d{4}-\d{2}-\d{2}$/', $value) !== 1) {
            return null;
        }

        [$year, $month, $day] = array_map('intval', explode('-', $value));
        if (! checkdate($month, $day, $year)) {
            return null;
        }

        return new self(self::toDayNumber($year, $month, $day));
    }

    public static function parse(string $value): self
    {
        return self::tryParse($value) ?? throw new \InvalidArgumentException("Not a yyyy-MM-dd date: {$value}");
    }

    public static function todayUtc(?DateTimeImmutable $now = null): self
    {
        $now = ($now ?? new DateTimeImmutable('now'))->setTimezone(new DateTimeZone('UTC'));

        return self::parse($now->format('Y-m-d'));
    }

    public function addDays(int $days): self
    {
        return new self($this->dayNumber + $days);
    }

    public function daysUntil(self $other): int
    {
        return $other->dayNumber - $this->dayNumber;
    }

    public function isBefore(self $other): bool
    {
        return $this->dayNumber < $other->dayNumber;
    }

    public function isAfter(self $other): bool
    {
        return $this->dayNumber > $other->dayNumber;
    }

    public function __toString(): string
    {
        return gmdate('Y-m-d', $this->dayNumber * 86400);
    }

    private static function toDayNumber(int $year, int $month, int $day): int
    {
        return intdiv((int) gmmktime(0, 0, 0, $month, $day, $year), 86400);
    }
}
