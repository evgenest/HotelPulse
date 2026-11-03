<?php

declare(strict_types=1);

use App\Http\Controllers\BookingController;
use App\Http\Controllers\HotelController;
use App\Http\Middleware\RateLimitBookings;
use Illuminate\Support\Facades\Route;

// Same routes as apps/api/Endpoints.cs; contract in docs/api.md.
Route::get('/hotels', [HotelController::class, 'index']);
Route::get('/hotels/{id}', [HotelController::class, 'show']);
Route::get('/hotels/{id}/availability', [HotelController::class, 'availability']);

Route::post('/bookings', [BookingController::class, 'store'])->middleware(RateLimitBookings::class);
Route::get('/bookings', [BookingController::class, 'index']);
Route::get('/bookings/{id}', [BookingController::class, 'show']);
Route::post('/bookings/{id}/cancel', [BookingController::class, 'cancel']);
