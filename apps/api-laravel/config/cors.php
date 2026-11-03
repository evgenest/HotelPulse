<?php

declare(strict_types=1);

// Same policy as the .NET API: CORS_ORIGINS is a comma-separated list; empty
// or "*" allows any origin (local development only).

// Read the env var here: config files load in alphabetical order, so
// config('hotelpulse.…') would still be empty while this file loads.
$origins = array_values(array_filter(array_map('trim', explode(',', (string) env('CORS_ORIGINS', '')))));

return [
    'paths' => ['api/*', 'health', 'health/*'],
    'allowed_methods' => ['*'],
    'allowed_origins' => $origins === [] || $origins === ['*'] ? ['*'] : $origins,
    'allowed_origins_patterns' => [],
    'allowed_headers' => ['*'],
    'exposed_headers' => ['X-Cache', 'X-Request-Id', 'Location'],
    'max_age' => 600,
    'supports_credentials' => false,
];
