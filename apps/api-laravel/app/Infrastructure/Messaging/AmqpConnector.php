<?php

declare(strict_types=1);

namespace App\Infrastructure\Messaging;

use PhpAmqpLib\Channel\AMQPChannel;
use PhpAmqpLib\Connection\AbstractConnection;
use PhpAmqpLib\Connection\AMQPConnectionConfig;
use PhpAmqpLib\Connection\AMQPConnectionFactory;

/**
 * Opens RabbitMQ connections from AMQP_URL and declares the topology. Same
 * shape as the .NET BookingPublisher: a durable topic exchange, a durable
 * queue and one binding. Names come from config so this stack never shares a
 * queue with the .NET one.
 */
final readonly class AmqpConnector
{
    public function __construct(
        private string $url,
        private string $exchange,
        private string $queue,
        private string $routingKey,
    ) {}

    public static function fromConfig(): self
    {
        return new self(
            (string) config('hotelpulse.amqp_url'),
            (string) config('hotelpulse.exchange'),
            (string) config('hotelpulse.queue'),
            (string) config('hotelpulse.routing_key'),
        );
    }

    public function exchange(): string
    {
        return $this->exchange;
    }

    public function queue(): string
    {
        return $this->queue;
    }

    public function routingKey(): string
    {
        return $this->routingKey;
    }

    public function connect(): AbstractConnection
    {
        $parts = parse_url($this->url) ?: [];
        $config = new AMQPConnectionConfig;
        $config->setHost($parts['host'] ?? 'localhost');
        $config->setPort((int) ($parts['port'] ?? (($parts['scheme'] ?? '') === 'amqps' ? 5671 : 5672)));
        $config->setUser(rawurldecode($parts['user'] ?? 'guest'));
        $config->setPassword(rawurldecode($parts['pass'] ?? 'guest'));
        $vhost = isset($parts['path']) && $parts['path'] !== '/' ? rawurldecode(ltrim($parts['path'], '/')) : '/';
        $config->setVhost($vhost === '' ? '/' : $vhost);
        $config->setIsSecure(($parts['scheme'] ?? '') === 'amqps');
        $config->setConnectionTimeout(5.0);
        $config->setReadTimeout(10.0);
        $config->setWriteTimeout(10.0);
        $config->setHeartbeat(30);

        return AMQPConnectionFactory::create($config);
    }

    public function declareTopology(AMQPChannel $channel): void
    {
        $channel->exchange_declare($this->exchange, 'topic', false, true, false);
        $channel->queue_declare($this->queue, false, true, false, false);
        $channel->queue_bind($this->queue, $this->exchange, $this->routingKey);
    }
}
