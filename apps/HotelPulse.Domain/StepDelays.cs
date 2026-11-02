using System.Globalization;

namespace HotelPulse.Domain;

public static class StepDelays
{
    public static readonly int[] Default = [700, 800, 900];

    public static int[] Resolve(string? overrideMs)
    {
        if (string.IsNullOrWhiteSpace(overrideMs))
            return Default;

        if (!int.TryParse(overrideMs, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms < 0)
            return Default;

        return [ms, ms, ms];
    }
}
