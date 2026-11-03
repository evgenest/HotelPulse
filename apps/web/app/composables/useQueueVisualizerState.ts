// On phones the full panel covers most of the bottom of the screen, so it
// starts minimized there. Once the user expands or minimizes it, the cookie
// keeps their choice.
export const COMPACT_SCREEN_QUERY = '(max-width: 720px)'

export function isCompactScreen() {
  return import.meta.client && window.matchMedia(COMPACT_SCREEN_QUERY).matches
}

export function useQueueVisualizerState() {
  return useCookie<boolean>('hp.queue-visualizer-minimized', {
    default: () => isCompactScreen(),
  })
}
