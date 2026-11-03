<?php

declare(strict_types=1);

namespace App\Domain;

use RuntimeException;

/** A message that will never succeed (bad payload, unknown booking). */
final class PermanentProcessingException extends RuntimeException {}
