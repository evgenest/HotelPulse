<template>
  <div class="timeline">
    <div class="tl-title">Event log</div>
    <ol>
      <li
        v-for="(ev, i) in events"
        :key="i"
        :class="{ done: ev.done, current: ev.current }"
      >
        <span class="t">{{ ev.time ?? '—' }}</span>
        <span class="d" />
        <span class="label">{{ ev.label }}</span>
      </li>
    </ol>
  </div>
</template>

<script setup lang="ts">
import type { BookingEvent } from '~/composables/useApi'
defineProps<{ events: BookingEvent[] }>()
</script>
