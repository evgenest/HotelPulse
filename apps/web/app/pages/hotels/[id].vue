<template>
  <div :class="['shell', historyOpen && 'with-rail']">
    <div>
      <AppNavbar
        :queue-state="queueState"
        :history-open="historyOpen"
        @go-home="navigateTo('/')"
        @toggle-history="historyOpen = !historyOpen"
      />

      <main v-if="hotel" class="page">
        <!-- Breadcrumb -->
        <div class="breadcrumb">
          <a @click="navigateTo('/')">Hotels</a>
          <span class="sep">/</span>
          <span style="color: var(--ink)">{{ hotel.name }}</span>
          <span style="margin-left: auto" class="mono">GET /api/hotels/{{ hotel.id }}</span>
        </div>

        <!-- Hero image -->
        <div class="hotel-hero">
          <div
            class="ph-stripes"
            style="position:absolute;inset:0;opacity:.9;background-image:repeating-linear-gradient(135deg,color-mix(in oklab,var(--ink) 7%,transparent) 0,color-mix(in oklab,var(--ink) 7%,transparent) 1px,transparent 1px,transparent 16px)"
          />
          <span
            class="ph-glyph"
            style="position:absolute;top:18px;right:22px;font-family:var(--font-mono);font-size:12px;color:var(--muted)"
          >
            {{ hotel.glyph }} · hero.jpg · 1920×720
          </span>
          <div class="hh-overlay" />
          <div class="hh-meta">
            <div>
              <div class="name">{{ hotel.name }}</div>
              <div class="city">{{ hotel.city }}</div>
            </div>
            <div class="col" style="align-items:flex-end;gap:4px">
              <HotelRating :value="hotel.rating" />
              <span class="mono" style="font-size:12px;color:var(--muted)">{{ hotel.rooms.length }} room types</span>
            </div>
          </div>
        </div>

        <!-- Detail layout -->
        <div class="detail-layout">
          <!-- Left: description + rooms -->
          <div>
            <div class="eyebrow">About</div>
            <h2 class="h-2" style="margin-top:6px;margin-bottom:12px">A quiet, well-built place to sleep.</h2>
            <p style="color:var(--ink-2);line-height:1.55;margin:0;max-width:540px">{{ hotel.description }}</p>

            <div class="eyebrow" style="margin-top:28px">Amenities</div>
            <div class="amenity-chips">
              <span v-for="a in hotel.amenities" :key="a" class="amenity">{{ a }}</span>
            </div>

            <!-- Rooms -->
            <div class="rooms-section">
              <div class="between">
                <h3 class="h-3">Pick a room</h3>
                <span class="mono" style="font-size:12px;color:var(--muted)">{{ hotel.rooms.length }} options</span>
              </div>
              <div class="rooms-list">
                <RoomCard
                  v-for="room in hotel.rooms"
                  :key="room.id"
                  :room="room"
                  :selected="room.id === selectedRoomId"
                  @select="selectedRoomId = room.id"
                />
              </div>
            </div>
          </div>

          <!-- Right: sticky booking panel -->
          <aside class="book-panel">
            <div class="eyebrow">Booking</div>
            <div class="price-tag" style="margin-top:6px">
              <span class="num">${{ selectedRoom?.price }}</span>
              <span class="unit">/ night</span>
            </div>
            <div style="color:var(--muted);font-size:13px;margin-top:4px">{{ selectedRoom?.type }}</div>

            <div class="summary">
              <div class="row" style="justify-content:space-between"><span class="k">hotel</span><span>{{ hotel.id }}</span></div>
              <div class="row" style="justify-content:space-between"><span class="k">room</span><span>{{ selectedRoom?.id }}</span></div>
              <div class="row" style="justify-content:space-between"><span class="k">capacity</span><span>{{ selectedRoom?.capacity }} guests</span></div>
              <div class="row" style="justify-content:space-between"><span class="k">area</span><span>{{ selectedRoom?.sqm }} m²</span></div>
            </div>

            <button
              class="btn btn-primary btn-lg"
              style="width:100%;margin-top:14px"
              @click="showForm = true"
            >
              Book this room
            </button>
            <div class="mono" style="font-size:11px;color:var(--muted);margin-top:10px;text-align:center">
              Async confirmation · usually &lt; 3s
            </div>
          </aside>
        </div>
      </main>

      <main v-else class="page">
        <div class="empty-state">Hotel not found.</div>
      </main>
    </div>

    <HistoryRail
      v-if="historyOpen"
      :bookings="bookingHistory"
      :active-id="null"
      @open="(id) => navigateTo(`/bookings/${id}`)"
      @clear="clearHistory"
    />

    <QueueVisualizer :state="queueState" />

    <!-- Booking form modal -->
    <BookingForm
      v-if="showForm && hotel && selectedRoom"
      :hotel="hotel"
      :room="selectedRoom"
      @close="showForm = false"
      @submit="handleBookingSubmit"
    />
  </div>
</template>

<script setup lang="ts">
import { useQueueStore } from '~/composables/useQueueStore'
import { useBookingStore } from '~/composables/useBookingStore'
import type { Hotel } from '~/composables/useApi'

definePageMeta({ layout: false })

const route = useRoute()
const config = useRuntimeConfig()
const { queueState, onPublish } = useQueueStore()
const { bookingHistory, addBooking, clearHistory, startHistorySync } = useBookingStore()

const historyOpen = ref(false)
const showForm = ref(false)
let stopHistorySync: (() => void) | null = null

const { data: hotel } = await useAsyncData(`hotel-${route.params.id}`, () =>
  $fetch<Hotel>(`${config.public.apiBase}/api/hotels/${route.params.id}`)
)

const selectedRoomId = ref(hotel.value?.rooms[0]?.id ?? '')
const selectedRoom = computed(() => hotel.value?.rooms.find((r) => r.id === selectedRoomId.value))

async function handleBookingSubmit(data: {
  guestName: string; checkIn: string; checkOut: string; nights: number; total: number
}) {
  if (!hotel.value || !selectedRoom.value) return
  showForm.value = false

  try {
    const result = await $fetch<{ id: string; status: string }>(
      `${config.public.apiBase}/api/bookings`,
      {
        method: 'POST',
        body: {
          hotelId: hotel.value.id,
          roomId: selectedRoom.value.id,
          guestName: data.guestName,
          checkIn: data.checkIn,
          checkOut: data.checkOut,
          nights: data.nights,
          total: data.total,
        },
      }
    )

    onPublish()

    addBooking({
      id: result.id,
      hotelId: hotel.value.id,
      hotelName: hotel.value.name,
      roomType: selectedRoom.value.type,
      status: 'pending',
      checkIn: data.checkIn,
      checkOut: data.checkOut,
    })

    navigateTo(`/bookings/${result.id}`)
  } catch (err) {
    console.error('Booking failed:', err)
    alert('Booking failed. Is the API running?')
  }
}

onMounted(() => {
  stopHistorySync = startHistorySync({ immediate: true })
})

onUnmounted(() => {
  stopHistorySync?.()
})
</script>
