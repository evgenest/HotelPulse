<template>
  <div class="modal-bg" @click.self="$emit('close')">
    <form class="modal" @submit.prevent="handleSubmit">
      <div class="eyebrow">POST /api/bookings</div>
      <h2>Book {{ room.type }}</h2>
      <div class="sub">{{ hotel.name }} · {{ hotel.city }}</div>

      <div class="form-grid">
        <div class="field">
          <label>Guest name</label>
          <input
            v-model="guestName"
            placeholder="Anna Schmidt"
            autofocus
            required
          />
        </div>
        <div class="date-row">
          <div class="field">
            <label>Check in</label>
            <input type="date" v-model="checkIn" required />
          </div>
          <div class="field">
            <label>Check out</label>
            <input type="date" v-model="checkOut" required />
          </div>
        </div>
      </div>

      <!-- Summary -->
      <div
        style="
          margin-top: 18px; padding: 12px;
          font-family: var(--font-mono); font-size: 12px;
          border: 1px dashed var(--border); border-radius: 12px;
          color: var(--ink-2); display: grid; gap: 6px;
        "
      >
        <div class="between"><span style="color:var(--muted)">nights</span><span>{{ nights }}</span></div>
        <div class="between"><span style="color:var(--muted)">price/night</span><span>${{ room.price }}</span></div>
        <div
          class="between"
          style="padding-top: 6px; border-top: 1px dashed var(--border); margin-top: 4px"
        >
          <span style="color:var(--muted)">total</span>
          <span style="font-weight: 600">${{ total }}</span>
        </div>
      </div>

      <div class="actions">
        <button type="button" class="btn" @click="$emit('close')">Cancel</button>
        <button
          type="submit"
          class="btn btn-primary"
          :disabled="submitting || !guestName.trim()"
        >
          <span v-if="submitting" class="spinner" />
          {{ submitting ? 'Publishing…' : 'Confirm booking' }}
        </button>
      </div>
    </form>
  </div>
</template>

<script setup lang="ts">
import type { Hotel, Room } from '~/composables/useApi'

const props = defineProps<{ hotel: Hotel; room: Room }>()
const emit = defineEmits<{
  close: []
  submit: [payload: { guestName: string; checkIn: string; checkOut: string; nights: number; total: number }]
}>()

const guestName = ref('')
const today = new Date()
const addDays = (d: Date, n: number) => { const x = new Date(d); x.setDate(x.getDate() + n); return x }
const fmt = (d: Date) => d.toISOString().slice(0, 10)

const checkIn = ref(fmt(addDays(today, 14)))
const checkOut = ref(fmt(addDays(today, 17)))
const submitting = ref(false)

const nights = computed(() =>
  Math.max(1, Math.round((new Date(checkOut.value).getTime() - new Date(checkIn.value).getTime()) / 86400000))
)
const total = computed(() => nights.value * props.room.price)

function handleSubmit() {
  if (!guestName.value.trim()) return
  submitting.value = true
  setTimeout(() => {
    emit('submit', {
      guestName: guestName.value,
      checkIn: checkIn.value,
      checkOut: checkOut.value,
      nights: nights.value,
      total: total.value,
    })
  }, 250)
}
</script>
