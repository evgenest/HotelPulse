<template>
  <span class="rating" :aria-label="`${value} of 5`">
    <i v-for="n in 5" :key="n" :class="{ on: n <= value }" />
  </span>
</template>

<script setup lang="ts">
defineProps<{ value: number }>()
</script>
