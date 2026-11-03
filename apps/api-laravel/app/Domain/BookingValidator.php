<?php

declare(strict_types=1);

namespace App\Domain;

/** Same rules and messages as apps/HotelPulse.Domain/BookingValidator.cs. */
final class BookingValidator
{
    public const int MaxGuestNameLength = 120;

    /** @return list<ValidationError> */
    public static function validate(CreateBookingCommand $command, ?Room $room, LocalDate $today): array
    {
        $errors = [];
        $guestName = trim($command->guestName ?? '');
        if ($guestName === '') {
            $errors[] = new ValidationError('guestName', 'Guest name is required.');
        } elseif (mb_strlen($guestName) > self::MaxGuestNameLength) {
            $errors[] = new ValidationError('guestName', 'Guest name must be '.self::MaxGuestNameLength.' characters or fewer.');
        }

        $checkIn = LocalDate::tryParse($command->checkIn);
        $checkOut = LocalDate::tryParse($command->checkOut);
        if ($checkIn === null) {
            $errors[] = new ValidationError('checkIn', 'Check-in must be a date in yyyy-MM-dd format.');
        }
        if ($checkOut === null) {
            $errors[] = new ValidationError('checkOut', 'Check-out must be a date in yyyy-MM-dd format.');
        }
        if ($checkIn === null || $checkOut === null) {
            return $errors;
        }

        if (! $checkOut->isAfter($checkIn)) {
            $errors[] = new ValidationError('checkOut', 'Check-out must be after check-in.');

            return $errors;
        }

        if ($checkIn->isBefore($today)) {
            $errors[] = new ValidationError('checkIn', 'Check-in cannot be in the past.');
        }

        $nights = $checkIn->daysUntil($checkOut);
        if ($command->nights !== $nights) {
            $errors[] = new ValidationError('nights', "Nights must equal the stay length ({$nights}).");
        }

        if ($room === null) {
            $errors[] = new ValidationError('roomId', 'Room does not exist for this hotel.');
        } elseif ($command->total === null || ! Money::equals($command->total, Money::times($room->price, $nights))) {
            $errors[] = new ValidationError('total', 'Total must equal '.Money::display($room->price)." × {$nights} nights.");
        }

        return $errors;
    }
}
