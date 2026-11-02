using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotelPulse.Infrastructure;

public static class HotelPulseJson
{
    public static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
