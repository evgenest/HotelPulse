<?php

declare(strict_types=1);

namespace App\Domain;

enum FailureKind
{
    case Permanent;
    case Transient;
    case DatabaseUnavailable;
}
