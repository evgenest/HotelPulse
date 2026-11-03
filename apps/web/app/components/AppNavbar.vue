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
        </nav>
      </div>

      <div class="row gap-2">
        <span class="pill nav-status" title="API health">
          <span class="pill-dot" style="background: var(--ok)" />
          <span style="color: var(--muted)">api</span>&nbsp;200ms
        </span>
        <span class="pill nav-status" :title="queueState.workerOnline ? 'Worker online' : 'Worker offline'">
          <span
            class="pill-dot"
            :style="{ background: queueState.workerOnline ? 'var(--ok)' : 'var(--bad)' }"
          />
          <span style="color: var(--muted)">worker</span>&nbsp;{{ queueState.workerOnline ? 'up' : 'down' }}
        </span>
        <a
          class="btn btn-sm btn-ghost nav-github"
          :href="repoUrl"
          target="_blank"
          rel="noopener"
          aria-label="Source code on GitHub"
          title="Source code on GitHub"
        >
          <svg viewBox="0 0 16 16" width="18" height="18" fill="currentColor" aria-hidden="true">
            <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
          </svg>
        </a>
        <button
          class="btn btn-sm btn-ghost nav-history"
          :aria-expanded="historyOpen"
          :title="historyOpen ? 'Hide your bookings' : 'Show your bookings'"
          @click="$emit('toggle-history')"
        >
          <IconHistory />
          Bookings
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
const repoUrl = useRuntimeConfig().public.repoUrl as string
const isHotels = computed(() => route.name === 'index')
</script>
