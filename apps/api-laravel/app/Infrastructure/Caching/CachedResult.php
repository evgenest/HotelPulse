<?php

declare(strict_types=1);

namespace App\Infrastructure\Caching;

/** A read and where it came from: HIT, MISS or BYPASS (the X-Cache header). */
final readonly class CachedResult
{
    public function __construct(public mixed $value, public string $cacheStatus) {}
}
