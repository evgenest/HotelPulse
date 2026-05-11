<template>
  <!-- Minimized pill -->
  <button v-if="minimized" class="qv-mini" @click="minimized = false">
    <span style="width:6px;height:6px;border-radius:50%;background:var(--ok)" />
    rabbitmq · {{ state.inflight }} in flight
  </button>

  <!-- Full widget -->
  <div v-else class="qv">
    <div class="qv-hd">
      <div class="t">
        <span class="live" />
        rabbitmq · bookings.created
      </div>
      <button class="qv-min" title="Minimize" @click="minimized = true">–</button>
    </div>
    <div class="qv-body">
      <!-- Stage nodes -->
      <div class="qv-stage">
        <div class="node"><div class="h">api</div><div class="v">publisher</div></div>
        <div class="node"><div class="h">exchange</div><div class="v">hotelpulse</div></div>
        <div class="node"><div class="h">worker</div><div class="v">{{ state.workerOnline ? 'consuming' : 'offline' }}</div></div>
      </div>

      <!-- Message track -->
      <div class="qv-track">
        <div
          v-for="msg in messages"
          :key="msg.id"
          class="qv-msg"
          :style="{ animationPlayState: state.workerOnline ? 'running' : 'paused' }"
        >{{ msg.label }}</div>
      </div>

      <!-- Stats -->
      <div class="qv-stats">
        <div style="display:flex;flex-direction:column;gap:0">
          <div>published</div><div class="v mono">{{ state.totalPublished }}</div>
        </div>
        <div style="display:flex;flex-direction:column;gap:0">
          <div>in flight</div>
          <div class="v mono" :style="{ color: state.inflight > 0 ? 'var(--accent)' : 'var(--ink)' }">{{ state.inflight }}</div>
        </div>
        <div style="display:flex;flex-direction:column;gap:0">
          <div>ack'd</div><div class="v mono">{{ state.totalAcked }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useQueueStore } from '~/composables/useQueueStore'

defineProps<{
  state: { totalPublished: number; totalAcked: number; inflight: number; workerOnline: boolean }
}>()

const { messages } = useQueueStore()
const minimized = ref(false)
</script>
