<?php

declare(strict_types=1);

use Illuminate\Database\Migrations\Migration;
use Illuminate\Support\Facades\DB;

// Runs database/sql/003_cancellation.sql, a byte-for-byte copy of the .NET migration
// (tests/Unit/MigrationsTest.php checks that). Never edit an applied
// migration: add a new numbered file in both places.
return new class extends Migration
{
    public $withinTransaction = true;

    public function up(): void
    {
        DB::unprepared((string) file_get_contents(database_path('sql/003_cancellation.sql')));
    }
};
