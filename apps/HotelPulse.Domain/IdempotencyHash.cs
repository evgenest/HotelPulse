using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HotelPulse.Domain;

public static class IdempotencyHash
{
    public static string Compute(CreateBookingCommand command)
    {
        var canonical = string.Join('\n',
            command.HotelId.Trim(),
            command.RoomId.Trim(),
            command.GuestName.Trim(),
            command.CheckIn.Trim(),
            command.CheckOut.Trim(),
            command.Nights.ToString(CultureInfo.InvariantCulture),
            command.Total.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
