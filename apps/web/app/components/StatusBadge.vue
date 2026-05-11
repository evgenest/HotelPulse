<template>
  <span class="pill" :data-status="status">
    <span class="pill-dot" />{{ status }}
  </span>
</template>

<script setup lang="ts">
defineProps<{ status: string }>()
</script>
