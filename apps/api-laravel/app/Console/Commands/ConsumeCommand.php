<?php

declare(strict_types=1);

namespace App\Console\Commands;

use App\Domain\BookingMessage;
use App\Domain\BookingProcessor;
use App\Domain\DeliveryPolicy;
use App\Domain\QueueAction;
use App\Infrastructure\Caching\HotelCache;
use App\Infrastructure\ExceptionClassifier;
use App\Infrastructure\Messaging\AmqpConnector;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Log;
use PhpAmqpLib\Exception\AMQPTimeoutException;
use PhpAmqpLib\Message\AMQPMessage;
use Throwable;

/**
 * The booking worker. One message at a time (prefetch 1), acked only after
 * the booking reaches a terminal status. Failures follow DeliveryPolicy:
 * a poison message is discarded, a transient error is requeued once and then
 * the booking is rejected, a database outage is always requeued.
 * Same behaviour as the .NET BookingConsumer.
 */
final class ConsumeCommand extends Command
{
    protected $signature = 'hotelpulse:consume';

    protected $description = 'Consume booking messages and confirm or reject bookings';

    private bool $stopping = false;

    public function handle(BookingProcessor $processor, HotelCache $cache): int
    {
        $this->trapSignals();
        $connector = AmqpConnector::fromConfig();

        while (! $this->stopRequested()) {
            try {
                $this->consume($connector, $processor, $cache);
            } catch (Throwable $e) {
                Log::error('RabbitMQ error; reconnecting in 5s', ['error' => $e->getMessage()]);
                for ($waited = 0; $waited < 5000 && ! $this->stopRequested(); $waited += 100) {
                    usleep(100_000);
                }
            }
        }

        Log::info('Consumer stopped');

        return self::SUCCESS;
    }

    private function consume(AmqpConnector $connector, BookingProcessor $processor, HotelCache $cache): void
    {
        $connection = $connector->connect();
        $channel = $connection->channel();
        try {
            $connector->declareTopology($channel);
            $channel->basic_qos(0, 1, false);
            $channel->basic_consume(
                $connector->queue(),
                '',
                false,
                false,
                false,
                false,
                fn (AMQPMessage $delivery) => $this->handleDelivery($delivery, $processor, $cache),
            );
            Log::info('Consuming', ['queue' => $connector->queue()]);

            while (! $this->stopRequested() && $channel->is_consuming()) {
                try {
                    $channel->wait(null, false, 1.0);
                } catch (AMQPTimeoutException) {
                    // No message within a second: check the stop flag and wait again.
                }
            }
        } finally {
            try {
                $channel->close();
                $connection->close();
            } catch (Throwable) {
                // Best effort on shutdown.
            }
        }
    }

    private function handleDelivery(AMQPMessage $delivery, BookingProcessor $processor, HotelCache $cache): void
    {
        $bookingId = null;
        $hotelId = null;
        try {
            $peek = json_decode($delivery->getBody(), true);
            $bookingId = is_array($peek) && is_string($peek['bookingId'] ?? null) ? $peek['bookingId'] : null;
            $hotelId = is_array($peek) && is_string($peek['hotelId'] ?? null) ? $peek['hotelId'] : null;

            $message = BookingMessage::fromJson($delivery->getBody());
            $result = $processor->process($message);
            if ($result->changed) {
                $cache->invalidateHotel($message->hotelId);
            }

            Log::info('Booking processed', ['bookingId' => $message->bookingId, 'status' => $result->status]);
            $delivery->ack();
        } catch (Throwable $e) {
            Log::error('Error processing booking', ['bookingId' => $bookingId, 'error' => $e->getMessage()]);
            $plan = DeliveryPolicy::decide(ExceptionClassifier::classify($e), (bool) $delivery->isRedelivered());

            if ($plan->markRejected && $bookingId !== null) {
                try {
                    $processor->markFailed($bookingId);
                    if ($hotelId !== null && $hotelId !== '') {
                        $cache->invalidateHotel($hotelId);
                    }
                } catch (Throwable $markError) {
                    Log::error('Could not mark booking failed; requeueing', ['bookingId' => $bookingId, 'error' => $markError->getMessage()]);
                    $delivery->nack(true);

                    return;
                }
            }

            match ($plan->action) {
                QueueAction::Ack => $delivery->ack(),
                QueueAction::Requeue => $delivery->nack(true),
                QueueAction::Discard => $delivery->nack(false),
            };
        }
    }

    /**
     * Handles pending signals, then reports whether one asked us to stop.
     * The answer can change between two calls.
     *
     * @phpstan-impure
     */
    private function stopRequested(): bool
    {
        if (function_exists('pcntl_signal_dispatch')) {
            pcntl_signal_dispatch();
        }

        return $this->stopping;
    }

    /** SIGTERM / SIGINT: finish the current message, then exit (graceful shutdown). */
    private function trapSignals(): void
    {
        if (! function_exists('pcntl_async_signals')) {
            return;
        }
        pcntl_async_signals(true);
        $stop = function (): void {
            $this->stopping = true;
        };
        pcntl_signal(SIGTERM, $stop);
        pcntl_signal(SIGINT, $stop);
    }
}
