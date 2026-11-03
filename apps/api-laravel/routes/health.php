<?php

declare(strict_types=1);

use App\Http\Controllers\HealthController;
use Illuminate\Support\Facades\Route;

Route::get('/health', [HealthController::class, 'ready']);
Route::get('/health/live', [HealthController::class, 'live']);
