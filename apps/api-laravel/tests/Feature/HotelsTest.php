<?php

declare(strict_types=1);

// docs/specs/laravel-port.md → API table, catalog rows.

use Illuminate\Support\Facades\DB;

it('reports ok and the version on /health when Postgres answers', function () {
    $this->getJson('/health')->assertOk()->assertJson(['status' => 'ok', 'version' => config('hotelpulse.version')]);
});

it('answers /health/live without touching dependencies', function () {
    $this->getJson('/health/live')->assertOk()->assertExactJson(['status' => 'ok']);
});

it('reports unavailable on /health when Postgres is down', function () {
    config(['database.connections.pgsql.url' => 'postgres://nobody:nothing@127.0.0.1:1/none']);
    DB::purge('pgsql');

    $this->getJson('/health')->assertStatus(503)->assertJson(['status' => 'unavailable']);
});

it('lists hotels with MISS first and HIT second', function () {
    $first = $this->getJson('/api/hotels')->assertOk()->assertHeader('X-Cache', 'MISS');
    $second = $this->getJson('/api/hotels')->assertOk()->assertHeader('X-Cache', 'HIT');

    expect($first->json())->toHaveCount(6)->toBe($second->json());
});

it('reads Postgres with X-Cache BYPASS when Redis is down', function () {
    breakRedis();

    $this->getJson('/api/hotels')->assertOk()->assertHeader('X-Cache', 'BYPASS')->assertJsonCount(6);
    $this->getJson('/api/hotels/h_porter')->assertOk()->assertHeader('X-Cache', 'BYPASS');
});

it('returns one hotel with its rooms', function () {
    $this->getJson('/api/hotels/h_porter')->assertOk()
        ->assertJsonPath('id', 'h_porter')
        ->assertJsonPath('rooms.0.id', 'r1')
        ->assertJsonPath('rooms.0.price', 150);
});

it('returns 404 for an unknown hotel', function () {
    $this->getJson('/api/hotels/nope')->assertNotFound()->assertExactJson(['error' => 'Hotel not found.']);
    $this->getJson('/api/hotels/nope/availability?checkIn=2030-01-01&checkOut=2030-01-02')->assertNotFound();
});

it('returns 400 for missing or malformed availability dates', function (string $query, string $field) {
    $this->getJson("/api/hotels/h_porter/availability{$query}")
        ->assertStatus(400)
        ->assertJsonPath('errors.0.field', $field);
})->with([
    ['', 'checkIn'],
    ['?checkIn=2030-01-01', 'checkOut'],
    ['?checkIn=01-01-2030&checkOut=2030-01-03', 'checkIn'],
    ['?checkIn=2030-01-03&checkOut=2030-01-03', 'checkOut'],
]);

it('shows a held room as unavailable and the others as available', function () {
    $body = bookingBody(40);
    $this->postJson('/api/bookings', $body)->assertStatus(202);

    $response = $this->getJson("/api/hotels/h_porter/availability?checkIn={$body['checkIn']}&checkOut={$body['checkOut']}")
        ->assertOk()
        ->assertJsonPath('hotelId', 'h_porter')
        ->assertJsonPath('checkIn', $body['checkIn']);

    $rooms = collect($response->json('rooms'))->keyBy('id');
    expect($rooms['r1']['available'])->toBeFalse()->and($rooms['r2']['available'])->toBeTrue();
});
