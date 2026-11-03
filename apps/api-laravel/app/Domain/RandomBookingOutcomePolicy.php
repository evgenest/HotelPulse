<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class RandomBookingOutcomePolicy implements BookingOutcomePolicy
{
    public function __construct(private float $confirmProbability) {}

    public function decide(): string
    {
        $roll = random_int(0, PHP_INT_MAX - 1) / PHP_INT_MAX;

        return BookingOutcome::fromRoll($roll, $this->confirmProbability);
    }
}
