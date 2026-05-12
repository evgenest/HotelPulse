export function useQueueVisualizerState() {
  return useCookie<boolean>('hp.queue-visualizer-minimized', {
    default: () => false,
  })
}
