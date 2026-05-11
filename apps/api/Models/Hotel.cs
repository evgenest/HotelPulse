using MongoDB.Bson.Serialization.Attributes;

namespace HotelPulse.Api.Models;

public record Room(
    string Id,
    string Type,
    int Capacity,
    decimal Price,
    int Sqm
);

public record Hotel(
    [property: BsonId] string Id,
    string Name,
    string City,
    int Rating,
    string Glyph,
    decimal PriceFrom,
    string Description,
    List<string> Amenities,
    List<Room> Rooms
);
