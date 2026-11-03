<?php

declare(strict_types=1);

use App\Http\Middleware\RequestId;
use App\Http\Middleware\SecurityHeaders;
use Illuminate\Foundation\Application;
use Illuminate\Foundation\Configuration\Exceptions;
use Illuminate\Foundation\Configuration\Middleware;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;
use Symfony\Component\HttpKernel\Exception\HttpExceptionInterface;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;

return Application::configure(basePath: dirname(__DIR__))
    ->withRouting(
        api: __DIR__.'/../routes/api.php',
        commands: __DIR__.'/../routes/console.php',
        then: function (): void {
            Route::middleware('api')->group(__DIR__.'/../routes/health.php');
        },
    )
    ->withMiddleware(function (Middleware $middleware): void {
        $middleware->prepend(RequestId::class);
        $middleware->append(SecurityHeaders::class);
    })
    ->withExceptions(function (Exceptions $exceptions): void {
        // Every error is JSON. Unknown routes are a plain 404; anything
        // unexpected is a 500 with no details (APP_DEBUG=false in production).
        $exceptions->shouldRenderJsonWhen(fn (Request $request) => true);

        $exceptions->render(function (NotFoundHttpException $e) {
            return response()->json(['error' => 'Not found.'], 404);
        });

        $exceptions->render(function (Throwable $e) {
            if ($e instanceof HttpExceptionInterface || config('app.debug')) {
                return null;
            }

            return response()->json(['error' => 'Internal server error.'], 500);
        });
    })->create();
