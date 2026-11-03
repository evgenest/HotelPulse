<?php

declare(strict_types=1);

namespace App\Http\Controllers;

use App\Domain\LocalDate;
use App\Domain\ValidationError;
use App\Infrastructure\Caching\CachedResult;
use App\Infrastructure\Caching\HotelCatalog;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;

final readonly class HotelController
{
    public function __construct(private HotelCatalog $catalog) {}

    public function index(): JsonResponse
    {
        return self::cached($this->catalog->list());
    }

    public function show(string $id): JsonResponse
    {
        $result = $this->catalog->get($id);

        return $result === null ? Errors::notFound('Hotel not found.') : self::cached($result);
    }

    public function availability(Request $request, string $id): JsonResponse
    {
        $checkIn = LocalDate::tryParse(self::query($request, 'checkIn'));
        if ($checkIn === null) {
            return Errors::validation([new ValidationError('checkIn', 'Check-in must be a date in yyyy-MM-dd format.')]);
        }
        $checkOut = LocalDate::tryParse(self::query($request, 'checkOut'));
        if ($checkOut === null) {
            return Errors::validation([new ValidationError('checkOut', 'Check-out must be a date in yyyy-MM-dd format.')]);
        }
        if (! $checkOut->isAfter($checkIn)) {
            return Errors::validation([new ValidationError('checkOut', 'Check-out must be after check-in.')]);
        }

        $result = $this->catalog->availability($id, $checkIn, $checkOut);
        if ($result === null) {
            return Errors::notFound('Hotel not found.');
        }

        return self::cached(new CachedResult([
            'hotelId' => $id,
            'checkIn' => (string) $checkIn,
            'checkOut' => (string) $checkOut,
            'rooms' => $result->value,
        ], $result->cacheStatus));
    }

    private static function cached(CachedResult $result): JsonResponse
    {
        return response()->json($result->value)->header('X-Cache', $result->cacheStatus);
    }

    private static function query(Request $request, string $name): ?string
    {
        $value = $request->query($name);

        return is_string($value) ? $value : null;
    }
}
