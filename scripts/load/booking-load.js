// Load smoke test for either HotelPulse API (docs/specs/laravel-port.md →
// «Нагрузочная проверка»). 50 virtual users for 60 s; about 80% reads and 20%
// bookings, aimed at a small set of rooms and dates so conflicts really happen.
//
//   BASE_URL=http://localhost:8080 k6 run scripts/load/booking-load.js
//
// Turn the API's booking rate limit off for this run
// (HOTELPULSE_BOOKING_RATE_LIMIT=0): every virtual user shares one IP.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const VUS = Number(__ENV.VUS || 50);
const DURATION = __ENV.DURATION || '60s';
// Bookings go FIRST_DAY+ days ahead (away from real demo data) into a
// WINDOW_DAYS window over every room. A year over all rooms gives both real
// inserts and conflicts; WINDOW_DAYS=14 ROOMS=2 makes almost every booking a
// conflict ("hot inventory").
const FIRST_DAY = Number(__ENV.FIRST_DAY || 400);
const WINDOW_DAYS = Number(__ENV.WINDOW_DAYS || 365);
const ROOMS = __ENV.ROOMS ? Number(__ENV.ROOMS) : Infinity;

const serverErrors = new Counter('server_errors');
const unexpectedStatus = new Counter('unexpected_status');
const created = new Counter('bookings_created');
const conflicts = new Counter('bookings_conflict');

export const options = {
  vus: VUS,
  duration: DURATION,
  thresholds: {
    server_errors: ['count==0'],
    unexpected_status: ['count==0'],
    'http_req_duration{kind:read}': ['p(95)<200'],
    'http_req_duration{kind:book}': ['p(95)<500'],
  },
  // Thresholds per kind need these sub-metrics to exist in the summary.
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// 409 is a correct answer for taken dates, so k6 should not count it as failed.
http.setResponseCallback(http.expectedStatuses(200, 202, 409));

export function setup() {
  const res = http.get(`${BASE}/api/hotels`);
  if (res.status !== 200) throw new Error(`GET /api/hotels returned ${res.status}`);
  return { hotels: res.json() };
}

function day(offset) {
  const d = new Date(Date.now() + offset * 86400000);
  return d.toISOString().slice(0, 10);
}

function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}

function record(res, ok) {
  if (res.status >= 500) serverErrors.add(1);
  if (!ok.includes(res.status)) unexpectedStatus.add(1, { status: String(res.status), url: res.request.url });
}

export default function (data) {
  const hotel = pick(data.hotels);

  if (Math.random() < 0.8) {
    const roll = Math.random();
    let res;
    if (roll < 0.3) {
      res = http.get(`${BASE}/api/hotels`, { tags: { kind: 'read', name: 'hotels' } });
    } else if (roll < 0.6) {
      res = http.get(`${BASE}/api/hotels/${hotel.id}`, { tags: { kind: 'read', name: 'hotel' } });
    } else {
      const start = FIRST_DAY + Math.floor(Math.random() * WINDOW_DAYS);
      res = http.get(`${BASE}/api/hotels/${hotel.id}/availability?checkIn=${day(start)}&checkOut=${day(start + 2)}`,
        { tags: { kind: 'read', name: 'availability' } });
    }
    record(res, [200]);
    check(res, { 'read is 200': (r) => r.status === 200 });
    return;
  }

  const room = pick(hotel.rooms.slice(0, ROOMS));
  const start = FIRST_DAY + Math.floor(Math.random() * WINDOW_DAYS);
  const nights = 1 + Math.floor(Math.random() * 3);
  const body = {
    hotelId: hotel.id,
    roomId: room.id,
    guestName: `Load ${__VU}-${__ITER}`,
    checkIn: day(start),
    checkOut: day(start + nights),
    nights,
    total: room.price * nights,
  };
  const res = http.post(`${BASE}/api/bookings`, JSON.stringify(body), {
    headers: { 'Content-Type': 'application/json' },
    tags: { kind: 'book', name: 'create booking' },
  });
  record(res, [202, 409]);
  if (res.status === 202) created.add(1);
  if (res.status === 409) conflicts.add(1);
  check(res, { 'booking is 202 or 409': (r) => r.status === 202 || r.status === 409 });
}

export function handleSummary(data) {
  const out = { stdout: textSummary(data) };
  if (__ENV.SUMMARY_JSON) out[__ENV.SUMMARY_JSON] = JSON.stringify(data, null, 2);
  return out;
}

function textSummary(data) {
  const m = data.metrics;
  const t = (name) => m[name] ? m[name].values : {};
  const ms = (v) => (v === undefined ? '-' : `${v.toFixed(1)} ms`);
  const read = t('http_req_duration{kind:read}');
  const book = t('http_req_duration{kind:book}');
  const failedThresholds = Object.entries(m)
    .filter(([, v]) => v.thresholds && Object.values(v.thresholds).some((x) => !x.ok))
    .map(([k]) => k);
  return [
    `requests: ${t('http_reqs').count} (${(t('http_reqs').rate || 0).toFixed(0)}/s)`,
    `reads  p95 ${ms(read['p(95)'])}  p99 ${ms(read['p(99)'])}  max ${ms(read.max)}`,
    `book   p95 ${ms(book['p(95)'])}  p99 ${ms(book['p(99)'])}  max ${ms(book.max)}`,
    `bookings created ${t('bookings_created').count || 0}, conflicts (409) ${t('bookings_conflict').count || 0}`,
    `5xx ${t('server_errors').count || 0}, unexpected statuses ${t('unexpected_status').count || 0}`,
    failedThresholds.length ? `THRESHOLDS FAILED: ${failedThresholds.join(', ')}` : 'all thresholds passed',
    '',
  ].join('\n');
}
