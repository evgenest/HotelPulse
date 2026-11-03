<?php

declare(strict_types=1);

// One version for the whole product: Directory.Build.props, apps/web/package.json
// and this app's config change together (root CLAUDE.md).

$root = dirname(__DIR__, 4);

it('reports the same version as the .NET projects and the web app', function () use ($root) {
    $config = require dirname(__DIR__, 2).'/config/hotelpulse.php';
    preg_match('#<Version>([^<]+)</Version>#', (string) file_get_contents("{$root}/Directory.Build.props"), $props);
    $web = json_decode((string) file_get_contents("{$root}/apps/web/package.json"), true);

    expect($config['version'])->toBe($props[1])->toBe($web['version']);
})->skip(! is_file($root.'/Directory.Build.props'), 'the repository root is not mounted');
