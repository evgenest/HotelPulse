namespace HotelPulse.Domain;

public enum FailureKind
{
    Permanent,
    Transient,
    DatabaseUnavailable,
}

public enum QueueAction
{
    Ack,
    Requeue,
    Discard,
}

public readonly record struct DeliveryPlan(QueueAction Action, bool MarkRejected);

/// <summary>
/// Decides what happens to a queue message after a processing failure.
/// A database outage always requeues, even on the second delivery, so the
/// durable message is not dropped while Postgres is unreachable. Any other
/// transient error is retried once and then the booking is rejected.
/// </summary>
public static class DeliveryPolicy
{
    public static DeliveryPlan Decide(FailureKind kind, bool redelivered) => kind switch
    {
        FailureKind.Permanent => new(QueueAction.Discard, true),
        FailureKind.DatabaseUnavailable => new(QueueAction.Requeue, false),
        FailureKind.Transient when !redelivered => new(QueueAction.Requeue, false),
        FailureKind.Transient => new(QueueAction.Discard, true),
        _ => new(QueueAction.Requeue, false),
    };
}
