<?php

declare(strict_types=1);

// The queue path: outbox relay → RabbitMQ → worker. Needs a real broker (AMQP_URL).

use App\Domain\BookingMessage;
use App\Domain\BookingProcessor;
use App\Domain\BookingStatus;
use App\Domain\ConfirmationCodes;
use App\Infrastructure\Messaging\AmqpConnector;
use Illuminate\Support\Facades\DB;

function purgeQueue(): void
{
    $connector = AmqpConnector::fromConfig();
    $connection = $connector->connect();
    $channel = $connection->channel();
    $connector->declareTopology($channel);
    $channel->queue_purge($connector->queue());
    $channel->close();
    $connection->close();
}

function waitForStatus(object $test, string $id, array $statuses, float $seconds = 15): array
{
    $deadline = microtime(true) + $seconds;
    do {
        $booking = $test->getJson("/api/bookings/{$id}")->json();
        if (in_array($booking['status'], $statuses, true)) {
            return $booking;
        }
        usleep(100_000);
    } while (microtime(true) < $deadline);

    throw new RuntimeException("Booking {$id} stayed {$booking['status']}.");
}

/** Starts the worker in its own process for the duration of $body. */
function withWorker(Closure $body): mixed
{
    $process = proc_open(
        [PHP_BINARY, base_path('artisan'), 'hotelpulse:consume'],
        [1 => ['file', '/dev/null', 'w'], 2 => ['file', '/dev/null', 'w']],
        $pipes,
        base_path(),
        getenv() + ['HOTELPULSE_STEP_DELAY_MS' => '0', 'HOTELPULSE_CONFIRM_PROBABILITY' => '1'],
    );
    try {
        return $body();
    } finally {
        proc_terminate($process, SIGTERM);
        proc_close($process);
    }
}

beforeEach(fn () => purgeQueue());

it('publishes the outbox row and the worker confirms the booking with a code', function () {
    $id = $this->postJson('/api/bookings', bookingBody(90))->json('id');

    $this->artisan('hotelpulse:relay', ['--once' => true])->assertSuccessful();
    expect(DB::table('outbox')->whereNull('published_at')->count())->toBe(0);

    $booking = withWorker(fn () => waitForStatus($this, $id, ['confirmed', 'rejected']));

    expect($booking['status'])->toBe('confirmed')
        ->and(ConfirmationCodes::isWellFormed($booking['confirmationCode']))->toBeTrue()
        ->and($booking['events'])->toHaveCount(5);
});

it('keeps outbox rows unpublished while the broker is down', function () {
    $this->postJson('/api/bookings', bookingBody(91))->assertStatus(202);
    config(['hotelpulse.amqp_url' => 'amqp://guest:guest@127.0.0.1:1/']);

    $this->artisan('hotelpulse:relay', ['--once' => true])->assertFailed();

    expect(DB::table('outbox')->whereNull('published_at')->count())->toBe(1);
});

it('ignores a redelivered message for a finished booking', function () {
    $id = $this->postJson('/api/bookings', bookingBody(92))->json('id');
    $processor = app(BookingProcessor::class);
    $message = new BookingMessage($id, 'h_porter', 'r1', new DateTimeImmutable);

    $first = $processor->process($message);
    $code = DB::table('bookings')->where('id', $id)->value('confirmation_code');
    $second = $processor->process($message);

    expect($first->changed)->toBeTrue()
        ->and($second->changed)->toBeFalse()
        ->and(DB::table('bookings')->where('id', $id)->value('confirmation_code'))->toBe($code);
});

it('leaves a booking cancelled before delivery cancelled', function () {
    $id = $this->postJson('/api/bookings', bookingBody(93))->json('id');
    $this->postJson("/api/bookings/{$id}/cancel")->assertOk();

    $result = app(BookingProcessor::class)->process(new BookingMessage($id, 'h_porter', 'r1', new DateTimeImmutable));

    expect($result->changed)->toBeFalse()
        ->and(DB::table('bookings')->where('id', $id)->value('status'))->toBe(BookingStatus::Cancelled);
});
