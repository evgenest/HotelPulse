<?php

declare(strict_types=1);

use Tests\TestCase;

// Feature tests boot the Laravel app; unit tests are plain PHP.
pest()->extend(TestCase::class)->in('Feature');
