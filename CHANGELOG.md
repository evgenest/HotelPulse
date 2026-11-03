# Changelog

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
