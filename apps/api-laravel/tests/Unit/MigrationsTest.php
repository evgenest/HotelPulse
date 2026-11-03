<?php

declare(strict_types=1);

// One schema for both implementations: the Laravel copies of the SQL
// migrations must match the .NET originals byte for byte.

$laravel = dirname(__DIR__, 2).'/database/sql';
$dotnet = dirname(__DIR__, 4).'/apps/HotelPulse.Infrastructure/Migrations';

it('has a copy of every .NET migration', function () use ($laravel, $dotnet) {
    expect(array_map('basename', glob("{$laravel}/*.sql")))->toBe(array_map('basename', glob("{$dotnet}/*.sql")));
})->skip(! is_dir($dotnet), 'the .NET sources are not next to this app');

it('keeps every copy identical to the .NET original', function (string $file) use ($laravel, $dotnet) {
    expect(hash_file('sha256', "{$laravel}/{$file}"))->toBe(hash_file('sha256', "{$dotnet}/{$file}"));
})->with(array_map('basename', glob($laravel.'/*.sql')))
    ->skip(! is_dir($dotnet), 'the .NET sources are not next to this app');
