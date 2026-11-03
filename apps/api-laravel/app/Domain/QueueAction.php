<?php

declare(strict_types=1);

namespace App\Domain;

enum QueueAction
{
    case Ack;
    case Requeue;
    case Discard;
}
