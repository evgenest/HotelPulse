export function useHistoryRailState() {
  return useCookie<boolean>('hp.history-rail-open', {
    default: () => false,
  })
}

/**
 * Set when the user opens the rail with the History button, so the rail can
 * scroll itself into view once it mounts. Not set when the rail is open on
 * page load from the cookie, so a reload does not jump to the bottom.
 */
export function useHistoryRailRevealRequest() {
  return useState<boolean>('hp.history-rail-reveal', () => false)
}

export function useToggleHistoryRail() {
  const open = useHistoryRailState()
  const reveal = useHistoryRailRevealRequest()

  return () => {
    open.value = !open.value
    reveal.value = open.value
  }
}
