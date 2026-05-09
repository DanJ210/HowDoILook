import { onUnmounted, ref } from 'vue'

export interface BackendRetryOptions {
  retryDelayMs?: number
  offlineMessage?: string
}

export function useBackendRequestState(options: BackendRetryOptions = {}) {
  const retryDelayMs = options.retryDelayMs ?? 3000
  const offlineMessage = options.offlineMessage ?? 'Waiting for backend to come online…'

  const isLoading = ref(false)
  const isLoadingMore = ref(false)
  const isWaitingForBackend = ref(false)
  const error = ref<string | null>(null)
  const retryCount = ref(0)

  let retryTimer: ReturnType<typeof setTimeout> | null = null

  function clearRetryTimer() {
    if (retryTimer) {
      clearTimeout(retryTimer)
      retryTimer = null
    }
  }

  function isOfflineError(err: unknown) {
    if (!err || typeof err !== 'object') return false
    const maybeError = err as { statusCode?: number }
    return maybeError.statusCode === 0 || typeof maybeError.statusCode === 'undefined'
  }

  function beginInitialLoad() {
    isLoading.value = true
    error.value = null
    isWaitingForBackend.value = false
    retryCount.value = 0
    clearRetryTimer()
  }

  function beginLoadMore() {
    isLoadingMore.value = true
  }

  function finishLoad() {
    isLoading.value = false
    isLoadingMore.value = false
  }

  function scheduleRetry(retryFn: () => Promise<void>) {
    if (retryTimer) return

    isWaitingForBackend.value = true
    retryTimer = setTimeout(async () => {
      retryTimer = null
      retryCount.value += 1
      await retryFn()
    }, retryDelayMs)
  }

  function handleError(err: unknown, retryFn?: () => Promise<void>, loadMore = false) {
    if (!loadMore && retryFn && isOfflineError(err)) {
      error.value = null
      scheduleRetry(retryFn)
      return
    }

    isWaitingForBackend.value = false
    error.value = (err as { message?: string })?.message ?? 'Failed to load data.'
  }

  function reset() {
    error.value = null
    isWaitingForBackend.value = false
    retryCount.value = 0
    clearRetryTimer()
  }

  onUnmounted(() => {
    clearRetryTimer()
  })

  return {
    isLoading,
    isLoadingMore,
    isWaitingForBackend,
    error,
    retryCount,
    offlineMessage,
    beginInitialLoad,
    beginLoadMore,
    finishLoad,
    handleError,
    reset,
    clearRetryTimer,
    isOfflineError
  }
}