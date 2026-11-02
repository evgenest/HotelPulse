namespace HotelPulse.Domain;

public interface IBookingOutcomePolicy
{
    string Decide();
}

public static class BookingOutcome
{
    public static string FromRoll(double roll, double confirmProbability)
    {
        if (roll is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(roll), "Roll must be in [0, 1).");
        if (confirmProbability is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confirmProbability), "Probability must be in [0, 1].");

        return roll < confirmProbability ? BookingStatuses.Confirmed : BookingStatuses.Rejected;
    }
}

public sealed class RandomBookingOutcomePolicy(double confirmProbability) : IBookingOutcomePolicy
{
    public string Decide() => BookingOutcome.FromRoll(Random.Shared.NextDouble(), confirmProbability);
}
