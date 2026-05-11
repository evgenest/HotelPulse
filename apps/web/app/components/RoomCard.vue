<template>
  <div class="room-card" :class="{ selected }" @click="$emit('select')">
    <div class="ph" />
    <div class="info">
      <div class="type">{{ room.type }}</div>
      <div class="meta">
        <span class="row gap-2">
          <svg viewBox="0 0 16 16" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.4"><circle cx="6" cy="5" r="2.2"/><path d="M2.5 13c.4-2 1.8-3 3.5-3s3.1 1 3.5 3"/><path d="M10 6.2A2 2 0 0 1 13 8c0 .4-.1.7-.3 1"/><path d="M11 13c-.1-1-.4-1.8-1-2.4"/></svg>
          sleeps {{ room.capacity }}
        </span>
        <span class="row gap-2">
          <svg viewBox="0 0 16 16" width="11" height="11" fill="none" stroke="currentColor" stroke-width="1.4"><rect x="2.5" y="2.5" width="11" height="11" rx="1"/></svg>
          {{ room.sqm }} m²
        </span>
      </div>
    </div>
    <div class="price">${{ room.price }}<small> / night</small></div>
    <button class="btn btn-sm" :class="{ 'btn-primary': selected }">
      {{ selected ? 'Selected' : 'Select' }}
    </button>
  </div>
</template>

<script setup lang="ts">
import type { Room } from '~/composables/useApi'

defineProps<{ room: Room; selected: boolean }>()
defineEmits<{ select: [] }>()
</script>
