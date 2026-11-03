<?php

declare(strict_types=1);

use App\Domain\BookingValidator;
use App\Domain\CreateBookingCommand;
use App\Domain\IdempotencyHash;
use App\Domain\LocalDate;
use App\Domain\Room;
use App\Domain\ValidationError;

function command(array $overrides = []): CreateBookingCommand
{
    $values = array_merge([
        'hotelId' => 'h_porter',
        'roomId' => 'r1',
        'guestName' => 'Anna Schmidt',
        'checkIn' => '2026-07-01',
        'checkOut' => '2026-07-04',
        'nights' => 3,
        'total' => '300',
    ], $overrides);

    return new CreateBookingCommand(...$values);
}

/** @return list<string> */
function failingFields(CreateBookingCommand $command): array
{
    $errors = BookingValidator::validate($command, new Room('r1', 'Standard', 2, '100.00', 20), LocalDate::parse('2026-06-01'));

    return array_map(static fn (ValidationError $e): string => $e->field, $errors);
}

it('accepts a stay whose nights and total match the room', fn () => expect(failingFields(command()))->toBe([]));
it('rejects a blank guest name', fn () => expect(failingFields(command(['guestName' => '   '])))->toContain('guestName'));
it('rejects a guest name over 120 characters', fn () => expect(failingFields(command(['guestName' => str_repeat('a', 121)])))->toContain('guestName'));
it('counts characters, not bytes, in the guest name', fn () => expect(failingFields(command(['guestName' => str_repeat('я', 120)])))->toBe([]));
it('rejects checkout on or before checkin', fn () => expect(failingFields(command(['checkOut' => '2026-07-01'])))->toContain('checkOut'));
it('rejects a night count that does not match the dates', fn () => expect(failingFields(command(['nights' => 2, 'total' => '200'])))->toContain('nights'));
it('rejects a total that does not match price times nights', fn () => expect(failingFields(command(['total' => '250'])))->toContain('total'));
it('accepts the total written with decimals', fn () => expect(failingFields(command(['total' => '300.00'])))->toBe([]));
it('rejects a checkin in the past', fn () => expect(failingFields(command(['checkIn' => '2026-05-01', 'checkOut' => '2026-05-03', 'nights' => 2, 'total' => '200'])))->toContain('checkIn'));
it('rejects dates that are not ISO', fn () => expect(failingFields(command(['checkIn' => '01-07-2026'])))->toContain('checkIn'));
it('rejects dates that do not exist', fn () => expect(failingFields(command(['checkOut' => '2026-02-30'])))->toContain('checkOut'));
it('trims the guest name before measuring it', fn () => expect(failingFields(command(['guestName' => '  Anna Schmidt  '])))->toBe([]));

it('prints the price like the .NET API in the total error', function () {
    $errors = BookingValidator::validate(command(['total' => '1']), new Room('r1', 'Standard', 2, '100.00', 20), LocalDate::parse('2026-06-01'));
    expect($errors[0]->message)->toBe('Total must equal 100.00 × 3 nights.');
});

it('hashes the same request to the same value', function () {
    expect(IdempotencyHash::compute(command(['guestName' => '  Anna Schmidt ', 'total' => '450'])))
        ->toBe(IdempotencyHash::compute(command(['total' => '450'])));
});

it('hashes a different guest as a different request', function () {
    expect(IdempotencyHash::compute(command(['guestName' => 'Boris'])))->not->toBe(IdempotencyHash::compute(command()));
});
