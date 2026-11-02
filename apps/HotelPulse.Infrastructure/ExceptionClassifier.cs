using System.Net.Sockets;
using System.Text.Json;
using HotelPulse.Domain;
using Npgsql;

namespace HotelPulse.Infrastructure;

public static class ExceptionClassifier
{
    public static FailureKind Classify(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PermanentProcessingException:
                case JsonException:
                    return FailureKind.Permanent;
                case NpgsqlException:
                case TimeoutException:
                case SocketException:
                    return FailureKind.DatabaseUnavailable;
            }
        }

        return FailureKind.Transient;
    }
}
