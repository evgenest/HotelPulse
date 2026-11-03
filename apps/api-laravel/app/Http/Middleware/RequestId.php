<?php

declare(strict_types=1);

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Log;
use Symfony\Component\HttpFoundation\Response;

/**
 * Takes X-Request-Id from the caller (or makes one), returns it, and adds it
 * to every log line written while the request runs.
 */
final class RequestId
{
    public function handle(Request $request, Closure $next): Response
    {
        $incoming = (string) $request->header('X-Request-Id', '');
        $id = preg_match('/^[A-Za-z0-9._-]{8,128}$/', $incoming) === 1 ? $incoming : bin2hex(random_bytes(16));

        // Octane keeps the process alive: reset the context for each request.
        Log::withoutContext();
        Log::withContext(['requestId' => $id]);

        $response = $next($request);
        $response->headers->set('X-Request-Id', $id);

        return $response;
    }
}
