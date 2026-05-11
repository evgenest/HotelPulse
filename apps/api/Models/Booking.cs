using MongoDB.Bson.Serialization.Attributes;

namespace HotelPulse.Api.Models;

public record BookingEvent(
    string Label,
    bool Done,
    string? Time,
    bool Current
);

public class Booking
{
    [BsonId]
    public string Id { get; set; } = "";
    public string HotelId { get; set; } = "";
    public string HotelName { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string RoomType { get; set; } = "";
    public string GuestName { get; set; } = "";
    public string CheckIn { get; set; } = "";
    public string CheckOut { get; set; } = "";
    public int Nights { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "pending";
    public string? ConfirmationCode { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<BookingEvent> Events { get; set; } = [];
}

public record CreateBookingRequest(
    string HotelId,
    string RoomId,
    string GuestName,
    string CheckIn,
    string CheckOut,
    int Nights,
    decimal Total
);

public record BookingMessage(
    string BookingId,
    string HotelId,
    string RoomId,
    DateTime CreatedAt
);
