<?php

declare(strict_types=1);

// HotelPulse settings. Same variables and meanings as the .NET services
// (apps/HotelPulse.Infrastructure/HotelPulseSettings.cs).

return [
    'amqp_url' => env('AMQP_URL', env('RABBITMQ_URI', 'amqp://guest:guest@localhost:5672/')),
    'exchange' => env('HOTELPULSE_EXCHANGE', 'hotelpulse.laravel'),
    'queue' => env('HOTELPULSE_QUEUE', 'bookings.created.laravel'),
    'routing_key' => 'booking.created',

    // Raw values; App\Domain\StepDelays and App\Domain\ConfirmProbability
    // parse them exactly like the .NET worker does.
    'step_delay_ms' => env('HOTELPULSE_STEP_DELAY_MS'),
    'confirm_probability' => env('HOTELPULSE_CONFIRM_PROBABILITY'),

    'cors_origins' => array_values(array_filter(array_map('trim', explode(',', (string) env('CORS_ORIGINS', ''))))),
];
