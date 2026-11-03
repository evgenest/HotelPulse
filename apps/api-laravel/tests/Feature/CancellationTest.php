<?php

declare(strict_types=1);

// The cases of docs/specs/booking-cancellation.md.

function createBooking(object $test, int $daysAhead): string
{
    return $test->postJson('/api/bookings', bookingBody($daysAhead))->assertStatus(202)->json('id');
}

it('cancels a pending booking and frees the room', function () {
    $body = bookingBody(80);
    $id = $this->postJson('/api/bookings', $body)->json('id');

    $this->postJson("/api/bookings/{$id}/cancel")->assertOk()
        ->assertJsonPath('status', 'cancelled')
        ->assertJsonPath('events.4.label', 'api → booking cancelled');
    expect($this->getJson("/api/bookings/{$id}")->json('cancelledAt'))->not->toBeNull();

    $this->postJson('/api/bookings', $body + ['guestName' => 'Boris'])->assertStatus(202);
});

it('cancels a confirmed booking and keeps its code', function () {
    $id = createBooking($this, 81);
    DB::update("UPDATE bookings SET status = 'confirmed', confirmation_code = 'HP-ABC123' WHERE id = ?", [$id]);

    $this->postJson("/api/bookings/{$id}/cancel")->assertOk()
        ->assertJsonPath('status', 'cancelled')
        ->assertJsonPath('confirmationCode', 'HP-ABC123');
});

it('succeeds and changes nothing on a second cancel', function () {
    $id = createBooking($this, 82);
    $first = $this->postJson("/api/bookings/{$id}/cancel")->json();
    $second = $this->postJson("/api/bookings/{$id}/cancel")->assertOk()->json();

    expect($second['cancelledAt'])->toBe($first['cancelledAt'])->and($second['events'])->toHaveCount(count($first['events']));
});

it('returns 404 for an unknown booking', function () {
    $this->postJson('/api/bookings/nope/cancel')->assertNotFound();
});

it('returns 409 for a rejected booking', function () {
    $id = createBooking($this, 83);
    DB::update("UPDATE bookings SET status = 'rejected', rejection_reason = 'x' WHERE id = ?", [$id]);

    $this->postJson("/api/bookings/{$id}/cancel")->assertStatus(409)
        ->assertExactJson(['error' => 'A rejected booking cannot be cancelled.']);
});

it('returns 409 on or after the check-in day', function () {
    $id = createBooking($this, 84);
    DB::update('UPDATE bookings SET check_in = current_date, check_out = current_date + 2, nights = 2 WHERE id = ?', [$id]);

    $this->postJson("/api/bookings/{$id}/cancel")->assertStatus(409)
        ->assertExactJson(['error' => 'Bookings can only be cancelled before the check-in date.']);
});
