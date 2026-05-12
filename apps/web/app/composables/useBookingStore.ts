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

function toHistoryEntry(booking: Booking, previous: HistoryEntry): HistoryEntry {
  return {
    ...previous,
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

async function refreshHistoryFromApi(apiBase: string, force = false) {
  if (import.meta.server || bookingHistory.value.length === 0) return

  const now = Date.now()
  if (!force && now - lastRefreshAt < REFRESH_TTL_MS) return
  if (refreshPromise) return refreshPromise

  const ids = [...new Set(bookingHistory.value.map((entry) => entry.id))].slice(0, MAX_HISTORY)
  if (ids.length === 0) return

  refreshPromise = $fetch<Booking[]>(`${apiBase}/api/bookings`, {
    query: { ids: ids.join(',') },
  })
    .then((bookings) => {
      lastRefreshAt = Date.now()

      const byId = new Map(bookings.map((booking) => [booking.id, booking]))
      let changed = false
      const next = bookingHistory.value.map((entry) => {
        const booking = byId.get(entry.id)
        if (!booking) return entry

        const updated = toHistoryEntry(booking, entry)
        if (!sameEntry(entry, updated)) {
          changed = true
        }
        return updated
      })

      if (changed) {
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

  function startHistorySync(options: { immediate?: boolean } = {}) {
    if (import.meta.server) return () => {}

    syncConsumers++
    installFocusListeners()

    if (options.immediate ?? true) {
      void refreshHistory({ force: true })
    }

    if (syncInterval === null) {
      syncInterval = setInterval(() => {
        if (document.visibilityState === 'visible') {
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
