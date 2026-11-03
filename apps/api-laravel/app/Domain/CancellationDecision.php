<?php

declare(strict_types=1);

namespace App\Domain;

enum CancellationDecision
{
    case Cancel;
    case AlreadyCancelled;
    case NotCancellable;
    case TooLate;
}
