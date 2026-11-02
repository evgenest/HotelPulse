using System.Globalization;

namespace HotelPulse.Domain;

public static class BookingValidator
{
    public const int MaxGuestNameLength = 120;

    public static IReadOnlyList<BookingValidationError> Validate(CreateBookingCommand command, Room? room, DateOnly today)
    {
        var errors = new List<BookingValidationError>();
        var guestName = command.GuestName?.Trim() ?? "";

        if (guestName.Length == 0)
            errors.Add(new("guestName", "Guest name is required."));
        else if (guestName.Length > MaxGuestNameLength)
            errors.Add(new("guestName", $"Guest name must be {MaxGuestNameLength} characters or fewer."));

        var checkInOk = DateOnly.TryParseExact(command.CheckIn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkIn);
        var checkOutOk = DateOnly.TryParseExact(command.CheckOut, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkOut);

        if (!checkInOk)
            errors.Add(new("checkIn", "Check-in must be a date in yyyy-MM-dd format."));
        if (!checkOutOk)
            errors.Add(new("checkOut", "Check-out must be a date in yyyy-MM-dd format."));
        if (!checkInOk || !checkOutOk)
            return errors;

        if (checkOut <= checkIn)
        {
            errors.Add(new("checkOut", "Check-out must be after check-in."));
            return errors;
        }

        if (checkIn < today)
            errors.Add(new("checkIn", "Check-in cannot be in the past."));

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (command.Nights != nights)
            errors.Add(new("nights", $"Nights must equal the stay length ({nights})."));

        if (room is null)
            errors.Add(new("roomId", "Room does not exist for this hotel."));
        else if (command.Total != room.Price * nights)
            errors.Add(new("total", $"Total must equal {room.Price} × {nights} nights."));

        return errors;
    }
}
