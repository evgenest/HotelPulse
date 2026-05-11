// Booking history stored in localStorage, loaded lazily on first access

const LS_KEY = 'hp.bookings'

interface HistoryEntry {
  id: string
  hotelId: string
  hotelName: string
  roomType: string
  status: string
  checkIn: string
  checkOut: string
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
    localStorage.setItem(LS_KEY, JSON.stringify(entries.slice(0, 20)))
  } catch {}
}

const bookingHistory = ref<HistoryEntry[]>([])

// Initialise once on client
if (import.meta.client) {
  bookingHistory.value = loadFromStorage()
}

export function useBookingStore() {
  function addBooking(entry: HistoryEntry) {
    bookingHistory.value = [entry, ...bookingHistory.value].slice(0, 20)
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

  return { bookingHistory, addBooking, updateStatus, clearHistory }
}
