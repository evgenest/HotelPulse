<template>
  <!-- Pipeline (default) -->
  <div v-if="variant === 'pipeline'" class="anim-pipeline">
    <div
      v-for="(name, i) in stages"
      :key="name"
      class="step"
      :class="{ done: i < stage, active: i === stage }"
    >
      <div class="ball">{{ i + 1 }}</div>
      <div class="label">{{ name }}</div>
    </div>
  </div>

  <!-- Ring -->
  <div v-else-if="variant === 'ring'" class="anim-ring">
    <div class="dot" />
  </div>

  <!-- Dots -->
  <div v-else class="anim-dots">
    <i /><i /><i />
  </div>
</template>

<script setup lang="ts">
withDefaults(defineProps<{
  variant?: 'pipeline' | 'ring' | 'dots'
  stage?: number
}>(), {
  variant: 'pipeline',
  stage: 0,
})

const stages = ['api', 'queue', 'worker'] as const
</script>
