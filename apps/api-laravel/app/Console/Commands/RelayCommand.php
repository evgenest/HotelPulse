<?php

declare(strict_types=1);

namespace App\Console\Commands;

use App\Domain\BookingMessage;
use App\Infrastructure\Messaging\AmqpConnector;
use App\Infrastructure\Messaging\BookingPublisher;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Throwable;

/**
 * Transactional outbox relay (ADR-0002). Takes up to 20 unpublished rows with
 * FOR UPDATE SKIP LOCKED, so several relays never publish the same row,
 * publishes each with confirms, and marks it published in the same
 * transaction. If the broker is down the rows stay and are retried.
 */
final class RelayCommand extends Command
{
    protected $signature = 'hotelpulse:relay {--once : Publish one batch and exit}';

    protected $description = 'Publish committed outbox rows to RabbitMQ';

    private bool $stopping = false;

    public function handle(): int
    {
        $this->trapSignals();
        $publisher = new BookingPublisher(AmqpConnector::fromConfig());
        Log::info('Outbox relay started');

        while (! $this->stopRequested()) {
            try {
                $published = $this->publishBatch($publisher);
                if ($this->option('once')) {
                    break;
                }
                if ($published === 0) {
                    $this->pause(200);
                }
            } catch (Throwable $e) {
                Log::error('Outbox relay failed; retrying', ['error' => $e->getMessage()]);
                $publisher->close();
                if ($this->option('once')) {
                    return self::FAILURE;
                }
                $this->pause(2000);
            }
        }

        $publisher->close();
        Log::info('Outbox relay stopped');

        return self::SUCCESS;
    }

    private function publishBatch(BookingPublisher $publisher): int
    {
        return DB::transaction(function () use ($publisher): int {
            $rows = DB::select(<<<'SQL'
                SELECT id, payload::text AS payload
                FROM outbox
                WHERE published_at IS NULL
                ORDER BY id
                FOR UPDATE SKIP LOCKED
                LIMIT 20
                SQL);

            foreach ($rows as $row) {
                $publisher->publish(BookingMessage::fromJson($row->payload));
                DB::update('UPDATE outbox SET published_at = now() WHERE id = :id', ['id' => $row->id]);
            }

            return count($rows);
        });
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

    /** SIGTERM / SIGINT: finish the current batch, then exit (graceful shutdown). */
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

    private function pause(int $ms): void
    {
        for ($waited = 0; $waited < $ms && ! $this->stopRequested(); $waited += 50) {
            usleep(50_000);
        }
    }
}
