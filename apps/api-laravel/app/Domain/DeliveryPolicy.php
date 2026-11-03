<?php

declare(strict_types=1);

namespace App\Domain;

/**
 * What happens to a queue message after a processing failure. A database
 * outage always requeues, even on the second delivery, so the message is not
 * dropped while Postgres is unreachable. Any other transient error is retried
 * once and then the booking is rejected.
 */
final class DeliveryPolicy
{
    public static function decide(FailureKind $kind, bool $redelivered): DeliveryPlan
    {
        return match (true) {
            $kind === FailureKind::Permanent => new DeliveryPlan(QueueAction::Discard, true),
            $kind === FailureKind::DatabaseUnavailable => new DeliveryPlan(QueueAction::Requeue, false),
            ! $redelivered => new DeliveryPlan(QueueAction::Requeue, false),
            default => new DeliveryPlan(QueueAction::Discard, true),
        };
    }
}
