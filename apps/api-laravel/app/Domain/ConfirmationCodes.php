<?php

declare(strict_types=1);

namespace App\Domain;

final class ConfirmationCodes
{
    /** "HP-" and six upper-case hex characters, like the .NET worker. */
    public static function create(?string $randomBytes = null): string
    {
        return 'HP-'.strtoupper(bin2hex($randomBytes ?? random_bytes(3)));
    }

    public static function isWellFormed(?string $code): bool
    {
        return $code !== null && preg_match('/^HP-[0-9A-F]{6}$/', $code) === 1;
    }
}
