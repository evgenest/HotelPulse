export interface Hotel {
  id: string
  name: string
  city: string
  rating: number
  glyph: string
  priceFrom: number
  description: string
  amenities: string[]
  rooms: Room[]
}

export interface Room {
  id: string
  type: string
  capacity: number
  price: number
  sqm: number
}

export interface BookingEvent {
  label: string
  done: boolean
  time: string | null
  current: boolean
}

export interface Booking {
  id: string
  hotelId: string
  hotelName: string
  roomId: string
  roomType: string
  guestName: string
  checkIn: string
  checkOut: string
  nights: number
  total: number
  status: 'pending' | 'confirmed' | 'rejected'
  confirmationCode?: string | null
  rejectionReason?: string | null
  createdAt: string
  events: BookingEvent[]
}

export interface CreateBookingPayload {
  hotelId: string
  roomId: string
  guestName: string
  checkIn: string
  checkOut: string
  nights: number
  total: number
}

export function useApi() {
  const config = useRuntimeConfig()
  const base = config.public.apiBase as string

  return {
    getHotels: () => $fetch<Hotel[]>(`${base}/api/hotels`),
    getHotel: (id: string) => $fetch<Hotel>(`${base}/api/hotels/${id}`),
    createBooking: (payload: CreateBookingPayload) =>
      $fetch<{ id: string; status: string }>(`${base}/api/bookings`, {
        method: 'POST',
        body: payload,
      }),
    getBooking: (id: string) => $fetch<Booking>(`${base}/api/bookings/${id}`),
  }
}
