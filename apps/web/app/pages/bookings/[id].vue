<template>
  <div :class="['shell', historyOpen && 'with-rail']">
    <div>
      <AppNavbar
        :queue-state="queueState"
        :history-open="historyOpen"
        @go-home="navigateTo('/')"
        @toggle-history="historyOpen = !historyOpen"
      />

      <main class="page">
        <!-- Breadcrumb -->
        <div v-if="booking" class="breadcrumb">
          <a @click="navigateTo('/')">Hotels</a>
          <span class="sep">/</span>
          <a @click="navigateTo(`/hotels/${booking.hotelId}`)">{{ booking.hotelName }}</a>
          <span class="sep">/</span>
          <span style="color: var(--ink)">Booking</span>
          <span style="margin-left: auto" class="mono">GET /api/bookings/{{ booking.id }}</span>
        </div>

        <div class="status-shell">
          <!-- Not found -->
          <div v-if="!booking && !loading" class="empty-state">
            <div class="h-2" style="color:var(--ink);margin-bottom:6px">Booking not found</div>
            <div>The booking <span class="mono">{{ route.params.id }}</span> doesn't exist.</div>
            <button class="btn btn-primary" style="margin-top:18px" @click="navigateTo('/')">Browse hotels</button>
          </div>

          <!-- Loading -->
          <div v-else-if="loading && !booking" class="status-card" style="padding:60px">
            <div class="spinner" style="width:32px;height:32px;margin:0 auto" />
          </div>

          <!-- Booking card -->
          <div v-else-if="booking" class="status-card">
            <div class="status-id">
              <span class="pill mono">{{ booking.id }}</span>
            </div>

            <!-- Status indicator -->
            <PendingAnimation v-if="booking.status === 'pending'" variant="pipeline" :stage="pendingStage" />

            <div v-else-if="booking.status === 'confirmed'" class="status-check">
              <svg viewBox="0 0 24 24" width="36" height="36" fill="none" stroke="var(--ok)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <path d="M5 12.5 10 17.5l9-10" />
              </svg>
            </div>

            <div v-else-if="booking.status === 'rejected'" class="status-x">
              <svg viewBox="0 0 24 24" width="36" height="36" fill="none" stroke="var(--bad)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <path d="M7 7l10 10M17 7l-10 10" />
              </svg>
            </div>

            <!-- Headline -->
            <h1 class="status-headline">
              <span v-if="booking.status === 'pending'">Processing your booking…</span>
              <span v-else-if="booking.status === 'confirmed'">Booking confirmed.</span>
              <span v-else>We couldn't confirm this booking.</span>
            </h1>
            <div class="status-sub">
              <span v-if="booking.status === 'pending'">Message published. Waiting on the worker.</span>
              <span v-else-if="booking.status === 'confirmed'">
                Confirmation #{{ booking.confirmationCode }} · details noted for {{ firstName }}.
              </span>
              <span v-else>{{ booking.rejectionReason ?? 'The room was just taken. Try another date or room.' }}</span>
            </div>

            <!-- Retry actions for rejected -->
            <div v-if="booking.status === 'rejected'" class="row gap-2" style="justify-content:center;margin-top:18px">
              <button class="btn btn-primary" @click="retry">Retry booking</button>
              <button class="btn" @click="navigateTo(`/hotels/${booking.hotelId}`)">Pick another room</button>
            </div>

            <!-- Summary -->
            <div class="status-summary">
              <div class="row"><span class="k">hotel</span><span class="v">{{ booking.hotelName }}</span></div>
              <div class="row"><span class="k">room</span><span class="v">{{ booking.roomType }}</span></div>
              <div class="row"><span class="k">guest</span><span class="v">{{ booking.guestName }}</span></div>
              <div class="row"><span class="k">stay</span><span class="v">{{ booking.checkIn }} → {{ booking.checkOut }} ({{ booking.nights }} nights)</span></div>
              <div class="row"><span class="k">total</span><span class="v">${{ booking.total }}</span></div>
            </div>

            <!-- Event timeline -->
            <EventTimeline :events="booking.events" />
          </div>
        </div>
      </main>
    </div>

    <HistoryRail
      v-if="historyOpen"
      :bookings="bookingHistory"
      :active-id="String(route.params.id)"
      @open="(id) => navigateTo(`/bookings/${id}`)"
      @clear="clearHistory"
    />

    <QueueVisualizer :state="queueState" />
  </div>
</template>

<script setup lang="ts">
import { useQueueStore } from '~/composables/useQueueStore'
import { useBookingStore } from '~/composables/useBookingStore'
import type { Booking } from '~/composables/useApi'

definePageMeta({ layout: false })

const route = useRoute()
const config = useRuntimeConfig()
const { queueState, onAck } = useQueueStore()
const { bookingHistory, updateStatus, clearHistory } = useBookingStore()

const historyOpen = ref(false)
const booking = ref<Booking | null>(null)
const loading = ref(true)

const firstName = computed(() => booking.value?.guestName.split(' ')[0] ?? '')

// Derive pipeline stage from events (how many are done)
const pendingStage = computed(() => {
  if (!booking.value) return 0
  const doneCount = booking.value.events.filter((e) => e.done).length
  // Stage maps to: 0=api received, 1=published to queue, 2=worker got it
  return Math.min(2, Math.max(0, doneCount - 1))
})

// Polling
let pollInterval: ReturnType<typeof setInterval> | null = null

async function fetchBooking() {
  try {
    const result = await $fetch<Booking>(
      `${config.public.apiBase}/api/bookings/${route.params.id}`
    )
    const wasTerminal = booking.value && booking.value.status !== 'pending'
    booking.value = result
    loading.value = false

    // Ack in queue visualizer and update history when status changes to terminal
    if (!wasTerminal && result.status !== 'pending') {
      onAck()
      updateStatus(result.id, result.status)
      stopPolling()
    }
  } catch {
    loading.value = false
  }
}

function stopPolling() {
  if (pollInterval !== null) {
    clearInterval(pollInterval)
    pollInterval = null
  }
}

onMounted(async () => {
  await fetchBooking()
  if (booking.value?.status === 'pending') {
    pollInterval = setInterval(fetchBooking, 1500)
  }
})

onUnmounted(stopPolling)

async function retry() {
  if (!booking.value) return
  // Navigate back to hotel and let user rebook
  navigateTo(`/hotels/${booking.value.hotelId}`)
}
</script>
