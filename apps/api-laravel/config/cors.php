<?php

declare(strict_types=1);

// Same policy as the .NET API: CORS_ORIGINS is a comma-separated list; empty
// or "*" allows any origin (local development only).

$origins = config('hotelpulse.cors_origins', []);

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
