namespace HotelPulse.Domain;

public sealed class PermanentProcessingException(string message) : Exception(message);

public sealed class RoomUnavailableException() : Exception("Those dates are already held for this room.");

public sealed class IdempotencyConflictException() : Exception("This Idempotency-Key was already used with a different request.");
