<?php

declare(strict_types=1);

namespace App\Domain;

interface BookingOutcomePolicy
{
    /** Returns BookingStatus::Confirmed or BookingStatus::Rejected. */
    public function decide(): string;
}
