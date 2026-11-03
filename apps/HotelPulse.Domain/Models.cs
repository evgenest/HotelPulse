namespace HotelPulse.Domain;

public sealed record Room(
    string Id,
    string Type,
    int Capacity,
    decimal Price,
    int Sqm);

public sealed record Hotel(
    string Id,
    string Name,
    string City,
    int Rating,
    string Glyph,
    decimal PriceFrom,
    string Description,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<Room> Rooms);

public sealed record BookingEvent(string Label, bool Done, string? Time, bool Current);

public sealed record Booking(
    string Id,
    string HotelId,
    string HotelName,
    string RoomId,
    string RoomType,
    string GuestName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    decimal Total,
    string Status,
    string? ConfirmationCode,
    string? RejectionReason,
    DateTime CreatedAt,
    IReadOnlyList<BookingEvent> Events,
    DateTime? CancelledAt = null);

public sealed record CreateBookingCommand(
    string HotelId,
    string RoomId,
    string GuestName,
    string CheckIn,
    string CheckOut,
    int Nights,
    decimal Total);

public sealed record CreateBookingRequest(
    string HotelId,
    string RoomId,
    string GuestName,
    string CheckIn,
    string CheckOut,
    int Nights,
    decimal Total);

public sealed record BookingMessage(
    string BookingId,
    string HotelId,
    string RoomId,
    DateTime CreatedAt);

public sealed record BookingValidationError(string Field, string Message);

public sealed record BookingCompletion(
    string Status,
    string? ConfirmationCode,
    string? RejectionReason,
    IReadOnlyList<BookingEvent> Events);

public abstract record ProcessingResult(string Status, string HotelId)
{
    public bool Changed => this is Completed;

    public sealed record Completed(string Status, string HotelId) : ProcessingResult(Status, HotelId);

    public sealed record Skipped(string Status, string HotelId) : ProcessingResult(Status, HotelId);
}

public sealed record RoomAvailability(
    string Id,
    string Type,
    int Capacity,
    decimal Price,
    int Sqm,
    bool Available);

public sealed record CachedResult<T>(T Value, string CacheStatus);
