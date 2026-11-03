<?php

declare(strict_types=1);

namespace App\Domain;

final readonly class DeliveryPlan
{
    public function __construct(public QueueAction $action, public bool $markRejected) {}
}
