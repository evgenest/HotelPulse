<?php

declare(strict_types=1);

use Illuminate\Support\Facades\Route;

// Routes are added endpoint by endpoint; see docs/specs/laravel-port.md.
Route::get('/ping', fn () => ['pong' => true]);
