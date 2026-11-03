<?php

declare(strict_types=1);

namespace App\Providers;

use App\Domain\BookingProcessor;
use App\Domain\Clock;
use App\Domain\ConfirmProbability;
use App\Domain\RandomBookingOutcomePolicy;
use App\Domain\StepDelays;
use App\Infrastructure\Postgres\BookingRepository;
use Illuminate\Support\ServiceProvider;

final class AppServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        // Stateless services only: Octane keeps them for the life of the worker.
        $this->app->singleton(BookingProcessor::class, fn ($app) => new BookingProcessor(
            $app->make(BookingRepository::class),
            new RandomBookingOutcomePolicy(ConfirmProbability::resolve(config('hotelpulse.confirm_probability'))),
            StepDelays::resolve(config('hotelpulse.step_delay_ms')),
            static function (int $ms): void {
                if ($ms > 0) {
                    usleep($ms * 1000);
                }
            },
            static fn () => Clock::nowUtc(),
        ));
    }

    public function boot(): void {}
}
