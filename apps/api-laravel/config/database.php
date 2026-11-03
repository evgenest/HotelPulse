<?php

declare(strict_types=1);

// Connection URLs use the same variable names as the .NET services
// (DATABASE_URL / POSTGRES_URI, REDIS_URL / REDIS_URI). See .env.example.

$text = static fn (mixed $value): ?string => is_string($value) && $value !== '' ? $value : null;

$databaseUrl = $text(env('DATABASE_URL')) ?? $text(env('POSTGRES_URI'));

$pgsql = static fn (?string $url): array => [
    'driver' => 'pgsql',
    'url' => $url,
    'charset' => 'utf8',
    'prefix' => '',
    'prefix_indexes' => true,
    'search_path' => 'public',
    'sslmode' => env('DB_SSLMODE', 'prefer'),
    'options' => [
        // Fail fast instead of hanging a worker when Postgres is unreachable.
        PDO::ATTR_TIMEOUT => 5,
    ],
];

return [

    'default' => 'pgsql',

    'connections' => [
        // Pooled URL for requests and workers. Only transaction-scoped
        // advisory locks are used, so PgBouncer transaction mode is fine.
        'pgsql' => $pgsql($databaseUrl),

        // Direct URL for migrations (session state, DDL).
        'pgsql_direct' => $pgsql($text(env('DATABASE_URL_UNPOOLED')) ?? $databaseUrl),
    ],

    'migrations' => [
        'table' => 'migrations',
        'update_date_on_publish' => true,
    ],

    'redis' => [
        'client' => 'phpredis',

        'options' => [
            'cluster' => 'redis',
            'prefix' => env('HOTELPULSE_REDIS_PREFIX', 'laravel:'),
        ],

        // Redis is only a cache: one quick attempt, then the API reads Postgres.
        'default' => [
            'url' => env('REDIS_URL', env('REDIS_URI')),
            'timeout' => 0.25,
            'read_timeout' => 0.25,
            'max_retries' => 0,
        ],
    ],

];
