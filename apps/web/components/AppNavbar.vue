<template>
  <header class="nav">
    <div class="nav-inner">
      <div class="row gap-5">
        <a class="brand" @click="$emit('go-home')">
          <span class="brand-mark" />
          <span>HotelPulse</span>
        </a>
        <nav class="nav-links">
          <a class="nav-link" :class="{ active: isHotels }" @click="$emit('go-home')">Hotels</a>
          <a class="nav-link">Channels</a>
          <a class="nav-link">Reports</a>
          <a class="nav-link">Settings</a>
        </nav>
      </div>

      <div class="row gap-2">
        <span class="pill" title="API health">
          <span class="pill-dot" style="background: var(--ok)" />
          <span style="color: var(--muted)">api</span>&nbsp;200ms
        </span>
        <span class="pill" :title="queueState.workerOnline ? 'Worker online' : 'Worker offline'">
          <span
            class="pill-dot"
            :style="{ background: queueState.workerOnline ? 'var(--ok)' : 'var(--bad)' }"
          />
          <span style="color: var(--muted)">worker</span>&nbsp;{{ queueState.workerOnline ? 'up' : 'down' }}
        </span>
        <button class="btn btn-sm btn-ghost" @click="$emit('toggle-history')">
          <IconHistory />
          {{ historyOpen ? 'Hide' : 'History' }}
        </button>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
defineProps<{
  activeRoute?: string | symbol | null
  queueState: { workerOnline: boolean }
  historyOpen: boolean
}>()

defineEmits<{
  'go-home': []
  'toggle-history': []
}>()

const route = useRoute()
const isHotels = computed(() => route.name === 'index')
</script>
