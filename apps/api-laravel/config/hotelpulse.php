<?php

declare(strict_types=1);

// HotelPulse settings. Same variables and meanings as the .NET services
// (apps/HotelPulse.Infrastructure/HotelPulseSettings.cs).

return [
    // Keep in step with Directory.Build.props and apps/web/package.json.
    'version' => '1.1.0',

    'amqp_url' => env('AMQP_URL', env('RABBITMQ_URI', 'amqp://guest:guest@localhost:5672/')),
    'exchange' => env('HOTELPULSE_EXCHANGE', 'hotelpulse.laravel'),
    'queue' => env('HOTELPULSE_QUEUE', 'bookings.created.laravel'),
    'routing_key' => 'booking.created',

    // Raw values; App\Domain\StepDelays and App\Domain\ConfirmProbability
    // parse them exactly like the .NET worker does.
    'step_delay_ms' => env('HOTELPULSE_STEP_DELAY_MS'),
    'confirm_probability' => env('HOTELPULSE_CONFIRM_PROBABILITY'),

    'booking_rate_limit_per_minute' => (int) env('HOTELPULSE_BOOKING_RATE_LIMIT', 30),
];
