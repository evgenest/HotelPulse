<template>
  <aside class="history-rail">
    <div class="h-hd">
      <div class="title">Bookings</div>
      <div class="row gap-2">
        <span class="mono" style="font-size:11px;color:var(--muted)">{{ bookings.length }}</span>
        <button
          v-if="bookings.length > 0"
          class="btn btn-sm btn-ghost"
          style="height:26px;padding:0 8px;font-size:12px"
          @click="$emit('clear')"
        >Clear</button>
      </div>
    </div>

    <div v-if="bookings.length === 0" class="empty">
      <div style="margin-bottom:6px">No bookings yet.</div>
      <div style="font-size:12px">Make one and you'll see the queue light up.</div>
    </div>

    <div v-else class="h-list">
      <div
        v-for="b in bookings"
        :key="b.id"
        class="h-item"
        :class="{ active: b.id === activeId }"
        @click="$emit('open', b.id)"
      >
        <div class="top">
          <span class="name">{{ b.hotelName }}</span>
          <StatusBadge :status="b.status" />
        </div>
        <div class="between">
          <span class="meta">{{ b.id }}</span>
          <span class="meta">{{ b.checkIn }} → {{ b.checkOut }}</span>
        </div>
      </div>
    </div>
  </aside>
</template>

<script setup lang="ts">
defineProps<{
  bookings: Array<{
    id: string
    hotelName: string
    status: string
    checkIn: string
    checkOut: string
  }>
  activeId: string | null
}>()

defineEmits<{ open: [id: string]; clear: [] }>()
</script>
