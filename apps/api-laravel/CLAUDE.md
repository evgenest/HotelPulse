# apps/api-laravel — notes for coding agents

Read the root `CLAUDE.md` first; its rules apply here unchanged. The spec for
this app is `docs/specs/laravel-port.md`; decisions are in `docs/adr/`.

- Same contract as the .NET API: `docs/api.md`. Change both or neither.
- Status changes are compare-and-set (`UPDATE ... WHERE status = ...`), never
  `$model->status = ...; $model->save()` after a read.
- Octane keeps the app in memory: no request state in static properties or
  singletons other than connection clients.
- Run everything in the PHP 8.5 image: `docker compose run --rm api-laravel ...`
  or the commands in the spec. Every file starts with `declare(strict_types=1);`.
