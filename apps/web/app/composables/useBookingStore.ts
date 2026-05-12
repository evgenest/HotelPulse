// Booking history stored in localStorage, loaded lazily on first access

import type { Booking } from './useApi'

const LS_KEY = 'hp.bookings'
const MAX_HISTORY = 20
const REFRESH_TTL_MS = 5000
const SYNC_INTERVAL_MS = 5000

export interface HistoryEntry {
  id: string
  hotelId: string
  hotelName: string
  roomId?: string
  roomType: string
  guestName?: string
  status: string
  checkIn: string
  checkOut: string
  nights?: number
  total?: number
  createdAt?: string
}

function loadFromStorage(): HistoryEntry[] {
  if (import.meta.server) return []
  try {
    return JSON.parse(localStorage.getItem(LS_KEY) ?? '[]')
  } catch {
    return []
  }
}

function saveToStorage(entries: HistoryEntry[]) {
  if (import.meta.server) return
  try {
    localStorage.setItem(LS_KEY, JSON.stringify(entries.slice(0, MAX_HISTORY)))
  } catch {}
}

const bookingHistory = ref<HistoryEntry[]>([])
let lastRefreshAt = 0
let refreshPromise: Promise<void> | null = null
let syncInterval: ReturnType<typeof setInterval> | null = null
let syncConsumers = 0
let focusListenersInstalled = false

// Initialise once on client
if (import.meta.client) {
  bookingHistory.value = loadFromStorage()
}

function toHistoryEntry(booking: Booking, previous?: HistoryEntry): HistoryEntry {
  return {
    ...(previous ?? {}),
    id: booking.id,
    hotelId: booking.hotelId,
    hotelName: booking.hotelName,
    roomId: booking.roomId,
    roomType: booking.roomType,
    guestName: booking.guestName,
    status: booking.status,
    checkIn: booking.checkIn,
    checkOut: booking.checkOut,
    nights: booking.nights,
    total: booking.total,
    createdAt: booking.createdAt,
  }
}

function sameEntry(a: HistoryEntry, b: HistoryEntry) {
  if (a.id !== b.id) return false

  return a.hotelId === b.hotelId
    && a.hotelName === b.hotelName
    && a.roomId === b.roomId
    && a.roomType === b.roomType
    && a.guestName === b.guestName
    && a.status === b.status
    && a.checkIn === b.checkIn
    && a.checkOut === b.checkOut
    && a.nights === b.nights
    && a.total === b.total
    && a.createdAt === b.createdAt
}

function sameHistoryEntries(a: HistoryEntry[], b: HistoryEntry[]) {
  if (a.length !== b.length) return false

  const byId = new Map(a.map((entry) => [entry.id, entry]))
  return b.every((entry) => {
    const previous = byId.get(entry.id)
    return previous !== undefined && sameEntry(previous, entry)
  })
}

async function refreshHistoryFromApi(apiBase: string, force = false) {
  if (import.meta.server) return

  const now = Date.now()
  if (!force && now - lastRefreshAt < REFRESH_TTL_MS) return
  if (refreshPromise) return refreshPromise

  refreshPromise = $fetch<Booking[]>(`${apiBase}/api/bookings`)
    .then((bookings) => {
      lastRefreshAt = Date.now()

      const previousById = new Map(bookingHistory.value.map((entry) => [entry.id, entry]))
      const next = bookings
        .slice(0, MAX_HISTORY)
        .map((booking) => toHistoryEntry(booking, previousById.get(booking.id)))

      if (!sameHistoryEntries(bookingHistory.value, next)) {
        bookingHistory.value = next
        saveToStorage(next)
      }
    })
    .catch(() => {})
    .finally(() => {
      refreshPromise = null
    })

  return refreshPromise
}

export function useBookingStore() {
  const config = useRuntimeConfig()
  const apiBase = config.public.apiBase as string

  function addBooking(entry: HistoryEntry) {
    bookingHistory.value = [entry, ...bookingHistory.value].slice(0, MAX_HISTORY)
    saveToStorage(bookingHistory.value)
  }

  function updateStatus(id: string, status: string) {
    const idx = bookingHistory.value.findIndex((b) => b.id === id)
    if (idx !== -1) {
      if (bookingHistory.value[idx].status === status) return
      bookingHistory.value[idx] = { ...bookingHistory.value[idx], status }
      saveToStorage(bookingHistory.value)
    }
  }

  function clearHistory() {
    bookingHistory.value = []
    saveToStorage([])
  }

  function refreshHistory(options: { force?: boolean } = {}) {
    return refreshHistoryFromApi(apiBase, options.force ?? false)
  }

  function installFocusListeners() {
    if (focusListenersInstalled || import.meta.server) return
    focusListenersInstalled = true

    window.addEventListener('focus', () => {
      void refreshHistory({ force: true })
    })
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        void refreshHistory({ force: true })
      }
    })
  }

  function startHistorySync(options: { immediate?: boolean; pollWhen?: () => boolean } = {}) {
    if (import.meta.server) return () => {}

    syncConsumers++
    installFocusListeners()

    if (options.immediate ?? true) {
      void refreshHistory({ force: true })
    }

    if (syncInterval === null) {
      syncInterval = setInterval(() => {
        if (document.visibilityState === 'visible' && (options.pollWhen?.() ?? true)) {
          void refreshHistory()
        }
      }, SYNC_INTERVAL_MS)
    }

    return () => {
      syncConsumers = Math.max(0, syncConsumers - 1)
      if (syncConsumers === 0 && syncInterval !== null) {
        clearInterval(syncInterval)
        syncInterval = null
      }
    }
  }

  return {
    bookingHistory,
    addBooking,
    updateStatus,
    clearHistory,
    refreshHistory,
    startHistorySync,
  }
}
