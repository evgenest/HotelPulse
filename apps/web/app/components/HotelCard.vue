<template>
  <article class="card hotel-card interactive" @click="$emit('open')">
    <div class="ph-image" style="height: 200px; border-radius: 0">
      <div class="ph-stripes" />
      <span class="ph-glyph">{{ hotel.glyph }}</span>
      <span class="ph-label">{{ labelName }}</span>
    </div>
    <div class="body">
      <div class="head-row">
        <div>
          <div class="name">{{ hotel.name }}</div>
          <div class="city">{{ hotel.city }}</div>
        </div>
        <HotelRating :value="hotel.rating" class="rating" />
      </div>
      <div class="footer-row">
        <div class="price">
          ${{ hotel.priceFrom }}<small>/ night, from</small>
        </div>
        <span class="mono" style="color: var(--muted); font-size: 12px">
          {{ hotel.rooms.length }} rooms
        </span>
      </div>
    </div>
  </article>
</template>

<script setup lang="ts">
import type { Hotel } from '~/composables/useApi'

const props = defineProps<{ hotel: Hotel }>()
defineEmits<{ open: [] }>()

const labelName = computed(() =>
  props.hotel.name.toLowerCase().replace(/\s+/g, '_') + '.jpg'
)
</script>
