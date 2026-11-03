<?php

declare(strict_types=1);

// docs/specs/laravel-port.md → API table, booking rows.

it('accepts a valid booking with 202, a Location and status pending', function () {
    $response = $this->postJson('/api/bookings', bookingBody(50))->assertStatus(202)->assertJsonPath('status', 'pending');
    $id = $response->json('id');

    expect($id)->toMatch('/^bkg_[0-9a-f]{12}$/');
    $response->assertHeader('Location', "/api/bookings/{$id}");
    expect(DB::table('outbox')->where('booking_id', $id)->count())->toBe(1);
});

it('invalidates the hotel cache when a booking is created', function () {
    $body = bookingBody(51);
    $url = "/api/hotels/h_porter/availability?checkIn={$body['checkIn']}&checkOut={$body['checkOut']}";
    $this->getJson($url)->assertHeader('X-Cache', 'MISS');
    $this->getJson($url)->assertHeader('X-Cache', 'HIT');

    $this->postJson('/api/bookings', $body)->assertStatus(202);

    $this->getJson($url)->assertHeader('X-Cache', 'MISS')->assertJsonPath('rooms.0.available', false);
});

it('returns 400 with field errors for an invalid body', function () {
    $this->postJson('/api/bookings', bookingBody(52, ['guestName' => '', 'nights' => 9, 'total' => 1]))
        ->assertStatus(400)
        ->assertJsonPath('errors.0.field', 'guestName')
        ->assertJsonPath('errors.1.field', 'nights');
});

it('returns 400 when there is no body', function () {
    $this->call('POST', '/api/bookings', [], [], [], ['CONTENT_TYPE' => 'application/json'], 'not json')
        ->assertStatus(400)
        ->assertExactJson(['errors' => [['field' => 'body', 'message' => 'Request body is required.']]]);
});

it('returns 404 for an unknown hotel or room', function () {
    $this->postJson('/api/bookings', bookingBody(53, ['hotelId' => 'nope']))->assertNotFound()->assertJson(['error' => 'Hotel not found.']);
    $this->postJson('/api/bookings', bookingBody(53, ['roomId' => 'r99']))->assertNotFound()->assertJson(['error' => 'Room not found.']);
});

it('returns 409 for dates already held on that room', function () {
    $this->postJson('/api/bookings', bookingBody(54))->assertStatus(202);
    $this->postJson('/api/bookings', bookingBody(55, ['guestName' => 'Boris']))
        ->assertStatus(409)
        ->assertExactJson(['error' => 'Those dates are already held for this room.']);
});

it('keeps the checkout morning free for the next guest', function () {
    $first = bookingBody(56);
    $this->postJson('/api/bookings', $first)->assertStatus(202);
    $this->postJson('/api/bookings', bookingBody(59, ['guestName' => 'Boris']))->assertStatus(202);
});

it('replays the original booking for the same key and body', function () {
    $body = bookingBody(60);
    $first = $this->postJson('/api/bookings', $body, ['Idempotency-Key' => 'k-1'])->assertStatus(202)->json('id');
    $second = $this->postJson('/api/bookings', $body, ['Idempotency-Key' => 'k-1'])->assertStatus(202)->json('id');

    expect($second)->toBe($first)->and(DB::table('bookings')->count())->toBe(1);
});

it('returns 409 for the same key with a different body', function () {
    $this->postJson('/api/bookings', bookingBody(61), ['Idempotency-Key' => 'k-2'])->assertStatus(202);
    $this->postJson('/api/bookings', bookingBody(70, ['guestName' => 'Boris']), ['Idempotency-Key' => 'k-2'])
        ->assertStatus(409)
        ->assertJson(['error' => 'This Idempotency-Key was already used with a different request.']);
});

it('returns 400 for an Idempotency-Key over 200 characters', function () {
    $this->postJson('/api/bookings', bookingBody(62), ['Idempotency-Key' => str_repeat('k', 201)])
        ->assertStatus(400)
        ->assertJsonPath('errors.0.field', 'Idempotency-Key');
});

it('lists the most recent bookings first', function () {
    $a = $this->postJson('/api/bookings', bookingBody(63, ['roomId' => 'r2', 'total' => 585]))->json('id');
    $b = $this->postJson('/api/bookings', bookingBody(63, ['roomId' => 'r3', 'total' => 855]))->json('id');

    expect(collect($this->getJson('/api/bookings')->assertOk()->json())->pluck('id')->all())->toBe([$b, $a]);
});

it('returns requested bookings in the order asked, skipping unknown ids', function () {
    $a = $this->postJson('/api/bookings', bookingBody(64, ['roomId' => 'r2', 'total' => 585]))->json('id');
    $b = $this->postJson('/api/bookings', bookingBody(64, ['roomId' => 'r3', 'total' => 855]))->json('id');

    expect(collect($this->getJson("/api/bookings?ids={$a},nope,{$b},{$a}")->assertOk()->json())->pluck('id')->all())->toBe([$a, $b]);
});

it('returns one booking in the contract shape', function () {
    $id = $this->postJson('/api/bookings', bookingBody(65))->json('id');

    $this->getJson("/api/bookings/{$id}")->assertOk()
        ->assertJsonPath('id', $id)
        ->assertJsonPath('hotelName', 'The Porter')
        ->assertJsonPath('total', 450)
        ->assertJsonPath('status', 'pending')
        ->assertJsonPath('events.0.label', 'api → POST /bookings received')
        ->assertJsonMissingPath('confirmationCode');
});

it('returns 404 for an unknown booking', function () {
    $this->getJson('/api/bookings/nope')->assertNotFound()->assertExactJson(['error' => 'Booking not found.']);
});

it('adds X-Request-Id and security headers to every response', function () {
    $this->getJson('/api/hotels', ['X-Request-Id' => 'abc-12345678'])
        ->assertHeader('X-Request-Id', 'abc-12345678')
        ->assertHeader('X-Content-Type-Options', 'nosniff')
        ->assertHeader('Referrer-Policy', 'no-referrer');
});

it('limits booking requests per client and lets them through without Redis', function () {
    config(['hotelpulse.booking_rate_limit_per_minute' => 2]);
    $this->postJson('/api/bookings', bookingBody(66, ['roomId' => 'r2', 'total' => 585]))->assertStatus(202);
    $this->postJson('/api/bookings', bookingBody(66, ['roomId' => 'r3', 'total' => 855]))->assertStatus(202);
    $this->postJson('/api/bookings', bookingBody(66, ['roomId' => 'r4', 'total' => 1080]))->assertStatus(429)->assertHeader('Retry-After');

    config(['database.redis.default.url' => 'redis://127.0.0.1:1']);
    Redis::purge();
    $this->postJson('/api/bookings', bookingBody(66, ['roomId' => 'r4', 'total' => 1080]))->assertStatus(202);
});
