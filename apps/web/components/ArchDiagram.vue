<template>
  <div class="arch">
    <div class="between" style="margin-bottom: 14px">
      <span class="eyebrow">Architecture</span>
      <span class="mono" style="font-size: 11px; color: var(--muted)">5 services</span>
    </div>
    <div class="arch-grid">
      <div class="arch-node"><span class="key">web</span><span class="val">Nuxt 3</span></div>
      <div class="arch-node hot"><span class="key">api</span><span class="val">.NET 8</span></div>
      <div class="arch-node"><span class="key">queue</span><span class="val">RabbitMQ</span></div>
      <div class="arch-node"><span class="key">worker</span><span class="val">BackgroundService</span></div>
      <div class="arch-node"><span class="key">db</span><span class="val">MongoDB</span></div>
      <div class="arch-node"><span class="key">orchestration</span><span class="val">Kubernetes</span></div>
    </div>
    <div class="arch-flow">
      <div class="step"><span class="idx">1</span>POST /bookings → 202 Accepted, status: Pending</div>
      <div class="step"><span class="idx">2</span>publish booking.created → exchange "hotelpulse"</div>
      <div class="step"><span class="idx">3</span>worker consumes, updates Mongo</div>
      <div class="step"><span class="idx">4</span>web polls /bookings/{id} until terminal</div>
    </div>
  </div>
</template>
