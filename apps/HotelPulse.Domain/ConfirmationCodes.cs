namespace HotelPulse.Domain;

public static class ConfirmationCodes
{
    public static string Create() => Create(Guid.NewGuid());

    public static string Create(Guid guid) => "HP-" + guid.ToString("N")[..6].ToUpperInvariant();

    public static bool IsWellFormed(string? code)
    {
        if (code is null || code.Length != 9 || !code.StartsWith("HP-", StringComparison.Ordinal))
            return false;

        for (var i = 3; i < code.Length; i++)
        {
            var c = code[i];
            var hex = c is >= '0' and <= '9' or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }

        return true;
    }
}
