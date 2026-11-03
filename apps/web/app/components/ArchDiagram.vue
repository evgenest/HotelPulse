<template>
  <div class="arch">
    <div class="between" style="margin-bottom: 14px">
      <span class="eyebrow">Architecture</span>
      <span class="mono" style="font-size: 11px; color: var(--muted)">6 services</span>
    </div>
    <div class="arch-grid">
      <div class="arch-node"><span class="key">web</span><span class="val">Nuxt 4</span></div>
      <div class="arch-node hot"><span class="key">api</span><span class="val">{{ stack.api }}</span></div>
      <div class="arch-node"><span class="key">queue</span><span class="val">RabbitMQ</span></div>
      <div class="arch-node"><span class="key">worker</span><span class="val">{{ stack.worker }}</span></div>
      <div class="arch-node"><span class="key">db</span><span class="val">PostgreSQL</span></div>
      <div class="arch-node"><span class="key">cache</span><span class="val">Redis</span></div>
    </div>
    <div class="arch-flow">
      <div class="step"><span class="idx">1</span>POST /bookings → 202 Accepted, status: Pending</div>
      <div class="step"><span class="idx">2</span>outbox publishes booking.created → exchange "{{ stack.exchange }}"</div>
      <div class="step"><span class="idx">3</span>worker consumes, writes the stay in PostgreSQL</div>
      <div class="step"><span class="idx">4</span>web polls /bookings/{id} until terminal</div>
    </div>
  </div>
</template>

<script setup lang="ts">
// The card follows the backend picked in the header (.NET or Laravel).
const { stack } = useBackend()
</script>
