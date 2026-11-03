# Changelog

## 1.0.3 — 2026-11-03

Concurrency audit before the Laravel port. Each fix has an ADR in `docs/adr/`.

- Cancelling a booking no longer drops timeline steps the worker wrote after
  the API read the row; the cancel event is appended inside the UPDATE
  (ADR-0003).
- The outbox relay waits for RabbitMQ publisher confirms and publishes with
  `mandatory: true`; a message the broker did not accept stays unpublished
  and is retried (ADR-0002).
- Parallel requests repeating one `Idempotency-Key` all get `202` with the
  original booking instead of some getting `409` (ADR-0004).
- Parallel bookings of one room no longer fail with `500 deadlock detected`;
  inserts for a room queue behind `pg_advisory_xact_lock` (ADR-0005).
- New `ConcurrencyTests`: parallel bookings, parallel idempotent replays,
  cancel racing the worker.
- Docs: architecture decision records (`docs/adr/`), Laravel port plan
  (`docs/plans/laravel-port.md`), work status (`docs/STATUS.md`).

## 1.0.2 — 2026-11-03

- Header: the history button is labelled "Bookings" again on every screen
  size, matching the panel it opens. It no longer switches to "Hide"; an open
  panel shows as a pressed button instead.
- Hero: the two buttons wrap on 320 px screens instead of overflowing.

## 1.0.1 — 2026-11-03

- Phones: the page no longer grows wider than the screen. The layout grid
  used plain `1fr` tracks, so the header and the architecture card pushed it
  past the viewport and the browser zoomed out; opening the history changed
  that width and looked like the whole page was resizing.
- Phones: opening the history scrolls to it, since it stacks under the page.
- Header: removed the Channels, Reports and Settings placeholders; on narrow
  screens the status pills are hidden and History is an icon button.

## 1.0.0 — 2026-11-03

- Booking cancellation: `POST /api/bookings/{id}/cancel`, migration
  `003_cancellation.sql`, a Cancel button on the booking page. Spec in
  `docs/specs/booking-cancellation.md`.
- PostgreSQL replaces MongoDB: exclusion constraint against double booking,
  transactional outbox, idempotency keys.
- Redis read-through cache for the catalog and availability.
- Unit and integration tests, GitHub Actions CI.
- Live demo on Render with Neon and CloudAMQP.
- `/health` reports the API version; the web app shows it in the hero.
- `CLAUDE.md` with the spec-first workflow for coding agents.

## 0.1.0 — 2026-05

- First demo: .NET API and worker, RabbitMQ, MongoDB, Nuxt front end, Docker
  Compose and Kubernetes manifests.
