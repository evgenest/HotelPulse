// Which API the app talks to. Both backends implement the same contract
// (docs/api.md) against their own database, queue and cache, so a booking id
// from one does not exist in the other: history is kept per backend.

export type BackendId = 'dotnet' | 'laravel'

/** What the architecture card and the queue widget show for each backend. */
export interface BackendStack {
  api: string
  worker: string
  exchange: string
  queue: string
}

export interface BackendOption {
  id: BackendId
  label: string
  apiBase: string
  stack: BackendStack
}

// Exchange and queue names match each API's config, so the two stacks
// never share a queue (docs/specs/laravel-port.md → isolation).
const STACKS: Record<BackendId, BackendStack> = {
  dotnet: { api: '.NET 10', worker: 'BackgroundService', exchange: 'hotelpulse', queue: 'bookings.created' },
  laravel: {
    api: 'Laravel 13',
    worker: 'artisan consume',
    exchange: 'hotelpulse.laravel',
    queue: 'bookings.created.laravel',
  },
}

const LS_KEY = 'hp.backend'

function readStored(): BackendId | null {
  try {
    const value = localStorage.getItem(LS_KEY)
    return value === 'dotnet' || value === 'laravel' ? value : null
  } catch {
    return null
  }
}

function store(id: BackendId) {
  try {
    localStorage.setItem(LS_KEY, id)
  } catch {
    // Private mode or blocked storage: the choice lasts for this tab only.
  }
}

export function useBackend() {
  const config = useRuntimeConfig().public
  const options = computed<BackendOption[]>(() => {
    const list: BackendOption[] = [
      { id: 'dotnet', label: '.NET', apiBase: config.apiBase as string, stack: STACKS.dotnet },
    ]
    const laravel = (config.apiBaseLaravel as string | undefined)?.trim()
    if (laravel) list.push({ id: 'laravel', label: 'Laravel', apiBase: laravel, stack: STACKS.laravel })
    return list
  })

  const selected = useState<BackendId>('hp-backend', () => readStored() ?? 'dotnet')
  const current = computed<BackendOption>(
    () => options.value.find((o) => o.id === selected.value) ?? options.value[0]!,
  )

  function select(id: BackendId) {
    if (!options.value.some((o) => o.id === id)) return
    selected.value = id
    store(id)
  }

  return {
    options,
    current,
    apiBase: computed(() => current.value.apiBase),
    stack: computed(() => current.value.stack),
    switchable: computed(() => options.value.length > 1),
    select,
  }
}
