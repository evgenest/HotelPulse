<?php

declare(strict_types=1);

namespace App\Http\Controllers;

use App\Domain\ValidationError;
use Illuminate\Http\JsonResponse;

/** Error bodies in the shapes docs/api.md promises. */
final class Errors
{
    /** @param list<ValidationError> $errors */
    public static function validation(array $errors): JsonResponse
    {
        return response()->json(['errors' => $errors], 400);
    }

    public static function notFound(string $message): JsonResponse
    {
        return response()->json(['error' => $message], 404);
    }

    public static function conflict(string $message): JsonResponse
    {
        return response()->json(['error' => $message], 409);
    }
}
