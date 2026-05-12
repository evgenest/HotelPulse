<template>
  <div :class="['shell', historyOpen && 'with-rail']">
    <div>
      <AppNavbar
        :queue-state="queueState"
        :history-open="historyOpen"
        @go-home="() => {}"
        @toggle-history="historyOpen = !historyOpen"
      />

      <main class="page">
        <!-- Hero: split layout -->
        <div class="hero hero-split">
          <div>
            <div class="eyebrow" style="margin-bottom: 16px">HotelPulse · v0.1.0</div>
            <h1 class="h-display">Bookings that just work, even when things don't.</h1>
            <p class="lede">
              Async-confirmed reservations with a durable queue between request and worker.
              If a service drops, your bookings don't.
            </p>
            <div class="row gap-2">
              <button class="btn btn-primary btn-lg" @click="scrollToHotels">Browse hotels</button>
              <a class="btn btn-lg" href="https://github.com" target="_blank">View architecture</a>
            </div>
          </div>
          <ArchDiagram />
        </div>

        <!-- Section header -->
        <div class="between" style="margin-bottom: 20px" ref="hotelsRef">
          <div>
            <div class="eyebrow">Available now</div>
            <div class="h-2" style="margin-top: 4px">{{ hotels?.length ?? 0 }} hotels across 6 cities</div>
          </div>
          <div class="row gap-2">
            <span class="pill mono">GET /api/hotels</span>
            <span class="pill" style="color: var(--muted)">200 OK · 42ms</span>
          </div>
        </div>

        <!-- Hotel grid -->
        <div v-if="pending" class="grid-hotels">
          <div v-for="i in 6" :key="i" class="card" style="height:290px">
            <div class="skel" style="height:200px;border-radius:0" />
            <div style="padding:16px;display:grid;gap:8px">
              <div class="skel" style="height:16px;width:60%" />
              <div class="skel" style="height:13px;width:40%" />
            </div>
          </div>
        </div>

        <div v-else-if="hotels" class="grid-hotels">
          <HotelCard
            v-for="hotel in hotels"
            :key="hotel.id"
            :hotel="hotel"
            @open="navigateTo(`/hotels/${hotel.id}`)"
          />
        </div>

        <div v-else class="empty-state">
          <div class="h-3">Could not load hotels</div>
          <div style="margin-top:8px;font-size:13px">Make sure the API is running on port 8080.</div>
          <button class="btn btn-primary" style="margin-top:16px" @click="refresh">Retry</button>
        </div>
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
  </div>
</template>

<script setup lang="ts">
import { useQueueStore } from '~/composables/useQueueStore'
import { useBookingStore } from '~/composables/useBookingStore'

definePageMeta({ layout: false })

const { queueState } = useQueueStore()
const { bookingHistory, clearHistory, refreshHistory, startHistorySync } = useBookingStore()

const historyOpen = useHistoryRailState()
const hotelsRef = ref<HTMLElement | null>(null)
let stopHistorySync: (() => void) | null = null

const config = useRuntimeConfig()
const { data: hotels, pending, refresh } = await useAsyncData('hotels', () =>
  $fetch<import('~/composables/useApi').Hotel[]>(`${config.public.apiBase}/api/hotels`)
)

function scrollToHotels() {
  hotelsRef.value?.scrollIntoView({ behavior: 'smooth' })
}

const stopHistoryOpenWatch = watch(historyOpen, (isOpen) => {
  if (isOpen) {
    void refreshHistory({ force: true })
  }
})

onMounted(() => {
  stopHistorySync = startHistorySync({
    immediate: true,
    pollWhen: () => historyOpen.value,
  })
})

onUnmounted(() => {
  stopHistoryOpenWatch()
  stopHistorySync?.()
})
</script>
