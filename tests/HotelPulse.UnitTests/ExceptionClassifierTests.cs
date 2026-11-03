using System.Net.Sockets;
using System.Text.Json;
using HotelPulse.Domain;
using HotelPulse.Infrastructure;
using Npgsql;

namespace HotelPulse.UnitTests;

public class ExceptionClassifierTests
{
    [Fact]
    public void A_missing_booking_is_permanent()
    {
        Assert.Equal(FailureKind.Permanent, ExceptionClassifier.Classify(new PermanentProcessingException("missing")));
    }

    [Fact]
    public void Invalid_json_is_permanent()
    {
        Assert.Equal(FailureKind.Permanent, ExceptionClassifier.Classify(new JsonException("bad")));
    }

    [Fact]
    public void A_postgres_failure_wrapped_by_the_host_is_still_a_database_outage()
    {
        var exception = new InvalidOperationException("worker failed", new NpgsqlException("connection refused"));
        Assert.Equal(FailureKind.DatabaseUnavailable, ExceptionClassifier.Classify(exception));
    }

    [Fact]
    public void A_socket_failure_is_a_database_outage()
    {
        Assert.Equal(FailureKind.DatabaseUnavailable, ExceptionClassifier.Classify(new SocketException()));
    }

    [Fact]
    public void An_unexpected_exception_is_retried()
    {
        Assert.Equal(FailureKind.Transient, ExceptionClassifier.Classify(new InvalidOperationException("bug")));
    }
}
