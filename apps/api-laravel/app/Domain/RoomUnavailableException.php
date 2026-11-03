<?php

declare(strict_types=1);

namespace App\Domain;

use RuntimeException;

final class RoomUnavailableException extends RuntimeException
{
    public function __construct()
    {
        parent::__construct('Those dates are already held for this room.');
    }
}
