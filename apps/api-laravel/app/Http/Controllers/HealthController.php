<?php

declare(strict_types=1);

namespace App\Http\Controllers;

use App\Domain\Clock;
use Illuminate\Http\JsonResponse;
use Illuminate\Support\Facades\DB;
use Throwable;

final class HealthController
{
    /** Readiness: 200 when Postgres answers SELECT 1, 503 otherwise. Redis is not required. */
    public function ready(): JsonResponse
    {
        try {
            DB::select('SELECT 1');
            $status = 'ok';
            $code = 200;
        } catch (Throwable) {
            $status = 'unavailable';
            $code = 503;
        }

        return response()->json([
            'status' => $status,
            'version' => config('hotelpulse.version'),
            'time' => Clock::toJson(Clock::nowUtc()),
        ], $code);
    }

    /** Liveness: the process answers. No dependencies, so a database outage does not restart it. */
    public function live(): JsonResponse
    {
        return response()->json(['status' => 'ok']);
    }
}
