<template>
  <div :class="['shell', historyOpen && 'with-rail']">
    <div>
      <AppNavbar
        :active-route="route.name"
        :queue-state="queueState"
        :history-open="historyOpen"
        @go-home="navigateTo('/')"
        @toggle-history="historyOpen = !historyOpen"
      />
      <slot />
    </div>

    <HistoryRail
      v-if="historyOpen"
      :bookings="bookingHistory"
      :active-id="currentBookingId"
      @open="(id) => navigateTo(`/bookings/${id}`)"
      @clear="clearHistory"
    />

    <QueueVisualizer :state="queueState" />
  </div>
</template>

<script setup lang="ts">
import { useQueueStore } from '~/composables/useQueueStore'
import { useBookingStore } from '~/composables/useBookingStore'

const route = useRoute()
const { queueState } = useQueueStore()
const { bookingHistory, clearHistory, startHistorySync } = useBookingStore()

const historyOpen = ref(false)
let stopHistorySync: (() => void) | null = null

const currentBookingId = computed(() =>
  route.name === 'bookings-id' ? String(route.params.id) : null
)

onMounted(() => {
  stopHistorySync = startHistorySync({ immediate: true })
})

onUnmounted(() => {
  stopHistorySync?.()
})
</script>
