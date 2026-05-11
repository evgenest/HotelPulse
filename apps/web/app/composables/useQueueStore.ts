// Module-level singleton — persists across page navigations in SPA mode
const queueState = reactive({
  totalPublished: 0,
  totalAcked: 0,
  inflight: 0,
  workerOnline: true,
})

const messages = ref<Array<{ id: string; label: string }>>([])

export function useQueueStore() {
  function onPublish() {
    queueState.totalPublished++
    queueState.inflight++

    const id = Math.random().toString(36).slice(2, 6)
    const label = String(queueState.totalPublished).padStart(2, '0')
    messages.value.push({ id, label })

    setTimeout(() => {
      messages.value = messages.value.filter((m) => m.id !== id)
    }, 2500)
  }

  function onAck() {
    queueState.totalAcked++
    queueState.inflight = Math.max(0, queueState.inflight - 1)
  }

  return { queueState, messages, onPublish, onAck }
}
