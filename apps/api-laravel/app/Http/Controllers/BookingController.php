<?php

declare(strict_types=1);

namespace App\Http\Controllers;

use App\Domain\Booking;
use App\Domain\BookingCancellation;
use App\Domain\BookingStatus;
use App\Domain\BookingTimeline;
use App\Domain\BookingValidator;
use App\Domain\CancellationDecision;
use App\Domain\Clock;
use App\Domain\CreateBookingCommand;
use App\Domain\IdempotencyConflictException;
use App\Domain\IdempotencyHash;
use App\Domain\LocalDate;
use App\Domain\Money;
use App\Domain\RoomUnavailableException;
use App\Domain\ValidationError;
use App\Infrastructure\Caching\HotelCache;
use App\Infrastructure\Postgres\BookingRepository;
use App\Infrastructure\Postgres\HotelRepository;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

/** Same cases and responses as apps/api/Endpoints.cs (docs/api.md → Bookings). */
final readonly class BookingController
{
    private const int MaxIdempotencyKeyLength = 200;

    private const int ListLimit = 20;

    public function __construct(
        private HotelRepository $hotels,
        private BookingRepository $bookings,
        private HotelCache $cache,
    ) {}

    public function store(Request $request): JsonResponse
    {
        $body = json_decode($request->getContent(), true);
        if (! is_array($body) || ! is_string($body['hotelId'] ?? null) || trim($body['hotelId']) === '') {
            return Errors::validation([new ValidationError('body', 'Request body is required.')]);
        }

        $hotel = $this->hotels->get($body['hotelId']);
        if ($hotel === null) {
            return Errors::notFound('Hotel not found.');
        }
        $room = $hotel->room(is_string($body['roomId'] ?? null) ? $body['roomId'] : '');
        if ($room === null) {
            return Errors::notFound('Room not found.');
        }

        $command = new CreateBookingCommand(
            $body['hotelId'],
            $room->id,
            is_string($body['guestName'] ?? null) ? $body['guestName'] : null,
            is_string($body['checkIn'] ?? null) ? $body['checkIn'] : null,
            is_string($body['checkOut'] ?? null) ? $body['checkOut'] : null,
            is_int($body['nights'] ?? null) ? $body['nights'] : null,
            Money::normalize($body['total'] ?? null),
        );
        $errors = BookingValidator::validate($command, $room, LocalDate::todayUtc());
        if ($errors !== []) {
            return Errors::validation($errors);
        }

        $idempotencyKey = trim((string) $request->header('Idempotency-Key', ''));
        if (mb_strlen($idempotencyKey) > self::MaxIdempotencyKeyLength) {
            return Errors::validation([new ValidationError('Idempotency-Key', 'Idempotency-Key must be 200 characters or fewer.')]);
        }

        $checkIn = LocalDate::parse((string) $command->checkIn);
        $checkOut = LocalDate::parse((string) $command->checkOut);
        $nights = $checkIn->daysUntil($checkOut);
        $now = Clock::nowUtc();
        $booking = new Booking(
            'bkg_'.bin2hex(random_bytes(6)),
            $hotel->id,
            $hotel->name,
            $room->id,
            $room->type,
            trim((string) $command->guestName),
            $checkIn,
            $checkOut,
            $nights,
            Money::times($room->price, $nights),
            BookingStatus::Pending,
            null,
            null,
            $now,
            BookingTimeline::initial($now),
        );

        try {
            [$created, $replay] = $this->bookings->create(
                $booking,
                $idempotencyKey === '' ? null : $idempotencyKey,
                IdempotencyHash::compute($command),
            );
        } catch (RoomUnavailableException|IdempotencyConflictException $e) {
            return Errors::conflict($e->getMessage());
        }

        if (! $replay) {
            $this->cache->invalidateHotel($created->hotelId);
        }

        return response()
            ->json(['id' => $created->id, 'status' => $created->status], 202)
            ->header('Location', "/api/bookings/{$created->id}");
    }

    public function index(Request $request): JsonResponse
    {
        $ids = $request->query('ids');
        if (! is_string($ids) || trim($ids) === '') {
            return response()->json($this->bookings->listRecent(self::ListLimit));
        }

        $requested = array_slice(array_values(array_unique(array_filter(
            array_map('trim', explode(',', $ids)),
            static fn (string $id): bool => $id !== '',
        ))), 0, self::ListLimit);

        return response()->json($this->bookings->getMany($requested));
    }

    public function show(string $id): JsonResponse
    {
        $booking = $this->bookings->find($id);

        return $booking === null ? Errors::notFound('Booking not found.') : response()->json($booking);
    }

    /**
     * Read, decide, then write with the same rules in the WHERE clause. If
     * another request changed the row in between, decide again on the fresh
     * row. Two rounds are enough: after a failed write the row is terminal or
     * past check-in, and neither can change again.
     */
    public function cancel(string $id): JsonResponse
    {
        for ($attempt = 0; $attempt < 2; $attempt++) {
            $booking = $this->bookings->find($id);
            if ($booking === null) {
                return Errors::notFound('Booking not found.');
            }

            $now = Clock::nowUtc();
            $today = LocalDate::todayUtc($now);
            $decision = BookingCancellation::decide($booking->status, $booking->checkIn, $today);

            if ($decision === CancellationDecision::AlreadyCancelled) {
                return response()->json($booking);
            }
            if ($decision !== CancellationDecision::Cancel) {
                return Errors::conflict((string) BookingCancellation::error($decision));
            }

            $cancelled = $this->bookings->tryCancel($id, $today, BookingTimeline::cancelledEvent($now));
            if ($cancelled === null) {
                continue;
            }

            $this->cache->invalidateHotel($cancelled->hotelId);

            return response()->json($cancelled);
        }

        return Errors::conflict('The booking changed while it was being cancelled. Try again.');
    }
}
