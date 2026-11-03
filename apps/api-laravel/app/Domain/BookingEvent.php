<?php

declare(strict_types=1);

namespace App\Domain;

use JsonSerializable;

/** One step of the booking timeline shown in the UI. */
final readonly class BookingEvent implements JsonSerializable
{
    public function __construct(
        public string $label,
        public bool $done,
        public ?string $time,
        public bool $current,
    ) {}

    public function with(?bool $done = null, ?string $time = null, ?bool $current = null): self
    {
        return new self($this->label, $done ?? $this->done, $time ?? $this->time, $current ?? $this->current);
    }

    /** @param array{label: string, done: bool, time?: ?string, current: bool} $data */
    public static function fromArray(array $data): self
    {
        return new self($data['label'], $data['done'], $data['time'] ?? null, $data['current']);
    }

    /** Same JSON as the .NET API: camelCase, null time omitted. */
    /** @return array<string, mixed> */
    public function jsonSerialize(): array
    {
        $json = ['label' => $this->label, 'done' => $this->done];
        if ($this->time !== null) {
            $json['time'] = $this->time;
        }
        $json['current'] = $this->current;

        return $json;
    }
}
