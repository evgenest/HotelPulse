<?php

declare(strict_types=1);

namespace App\Infrastructure\Messaging;

use App\Domain\BookingMessage;
use PhpAmqpLib\Channel\AMQPChannel;
use PhpAmqpLib\Connection\AbstractConnection;
use PhpAmqpLib\Message\AMQPMessage;
use RuntimeException;

/**
 * Publishes with publisher confirms and mandatory routing (ADR-0002):
 * publish() returns only after the broker acked the message, and throws if it
 * was nacked, returned as unroutable, or not confirmed in time. Only then may
 * the outbox row be marked published.
 */
final class BookingPublisher
{
    private ?AbstractConnection $connection = null;

    private ?AMQPChannel $channel = null;

    private bool $nacked = false;

    private bool $returned = false;

    public function __construct(private readonly AmqpConnector $connector) {}

    public function publish(BookingMessage $message): void
    {
        $channel = $this->channel();
        $this->nacked = false;
        $this->returned = false;

        $channel->basic_publish(
            new AMQPMessage(json_encode($message, JSON_THROW_ON_ERROR), [
                'content_type' => 'application/json',
                'delivery_mode' => AMQPMessage::DELIVERY_MODE_PERSISTENT,
            ]),
            $this->connector->exchange(),
            $this->connector->routingKey(),
            true,
        );
        $channel->wait_for_pending_acks_returns(5.0);

        if ($this->returned) {
            throw new RuntimeException("Broker returned booking {$message->bookingId}: no queue is bound.");
        }
        if ($this->nacked) {
            throw new RuntimeException("Broker nacked booking {$message->bookingId}.");
        }
    }

    public function close(): void
    {
        try {
            $this->channel?->close();
            $this->connection?->close();
        } catch (\Throwable) {
            // Closing a broken connection is best effort.
        }
        $this->channel = null;
        $this->connection = null;
    }

    private function channel(): AMQPChannel
    {
        if ($this->channel !== null && $this->channel->is_open()) {
            return $this->channel;
        }

        $this->close();
        $this->connection = $this->connector->connect();
        $channel = $this->connection->channel();
        $this->connector->declareTopology($channel);
        $channel->confirm_select();
        $channel->set_nack_handler(function (): void {
            $this->nacked = true;
        });
        $channel->set_return_listener(function (): void {
            $this->returned = true;
        });

        return $this->channel = $channel;
    }
}
