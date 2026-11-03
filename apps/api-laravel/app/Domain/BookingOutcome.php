<?php

declare(strict_types=1);

namespace App\Domain;

use InvalidArgumentException;

final class BookingOutcome
{
    public static function fromRoll(float $roll, float $confirmProbability): string
    {
        if ($roll < 0 || $roll >= 1) {
            throw new InvalidArgumentException('Roll must be in [0, 1).');
        }
        if ($confirmProbability < 0 || $confirmProbability > 1) {
            throw new InvalidArgumentException('Probability must be in [0, 1].');
        }

        return $roll < $confirmProbability ? BookingStatus::Confirmed : BookingStatus::Rejected;
    }
}
