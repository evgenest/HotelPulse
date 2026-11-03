<?php

declare(strict_types=1);

namespace App\Domain;

use JsonSerializable;

final readonly class ValidationError implements JsonSerializable
{
    public function __construct(public string $field, public string $message) {}

    /** @return array<string, mixed> */
    public function jsonSerialize(): array
    {
        return ['field' => $this->field, 'message' => $this->message];
    }
}
