<?php

declare(strict_types=1);

namespace App\Infrastructure;

use App\Domain\FailureKind;
use App\Domain\PermanentProcessingException;
use Illuminate\Database\QueryException;
use JsonException;
use PDOException;
use Throwable;

/**
 * Sorts a worker failure into permanent, transient or database-down, the
 * same way ExceptionClassifier.cs does. Walks the chain of previous
 * exceptions like .NET walks InnerException.
 */
final class ExceptionClassifier
{
    public static function classify(Throwable $exception): FailureKind
    {
        for ($current = $exception; $current !== null; $current = $current->getPrevious()) {
            if ($current instanceof PermanentProcessingException || $current instanceof JsonException) {
                return FailureKind::Permanent;
            }
            if ($current instanceof QueryException || $current instanceof PDOException) {
                return FailureKind::DatabaseUnavailable;
            }
        }

        return FailureKind::Transient;
    }
}
