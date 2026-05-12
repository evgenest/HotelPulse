export function useHistoryRailState() {
  return useCookie<boolean>('hp.history-rail-open', {
    default: () => false,
  })
}
