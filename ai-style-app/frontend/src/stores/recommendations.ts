import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api/client'
import type {
  CreateRecommendationsRequest,
  CreateRecommendationsResponse,
  RecommendationJobStatusResponse,
  SubmitRecommendationFeedbackRequest,
  UploadImageResponse
} from '@/types/api'
import { TERMINAL_STATUSES } from '@/types/api'

export const useRecommendationsStore = defineStore('recommendations', () => {
  const jobs = ref<Record<string, RecommendationJobStatusResponse>>({})
  const activePollingIds = ref<Set<string>>(new Set())
  const pollingState = ref<Record<string, 'polling' | 'stopped' | 'completed' | 'error'>>({})
  const pollingError = ref<Record<string, string | null>>({})
  const pollingTimers = ref<Record<string, ReturnType<typeof setTimeout> | null>>({})

  function clearPollingTimer(jobId: string) {
    const timer = pollingTimers.value[jobId]
    if (timer) {
      clearTimeout(timer)
    }
    pollingTimers.value[jobId] = null
  }

  function getJob(jobId: string): RecommendationJobStatusResponse | undefined {
    return jobs.value[jobId]
  }

  async function fetchStatus(jobId: string): Promise<RecommendationJobStatusResponse> {
    const job = await api.get<RecommendationJobStatusResponse>(`/recommendations/jobs/${jobId}`)
    jobs.value[jobId] = job
    return job
  }

  async function create(
    request: Omit<CreateRecommendationsRequest, 'imageUrl'> & { imageUrl?: string },
    imageFile?: File
  ): Promise<CreateRecommendationsResponse> {
    let imageUrl = request.imageUrl

    if (!imageUrl && imageFile) {
      const uploaded = await api.upload<UploadImageResponse>('/upload/image', imageFile)
      imageUrl = uploaded.url
    }

    if (!imageUrl) {
      throw { message: 'Image is required for recommendations.', statusCode: 400 }
    }

    return api.post<CreateRecommendationsResponse>('/recommendations', {
      ...request,
      imageUrl
    })
  }

  function startPolling(jobId: string, onComplete?: (job: RecommendationJobStatusResponse) => void) {
    if (activePollingIds.value.has(jobId)) return

    activePollingIds.value.add(jobId)
    pollingState.value[jobId] = 'polling'
    pollingError.value[jobId] = null
    clearPollingTimer(jobId)

    let intervalMs = 2000
    const maxIntervalMs = 10000

    const poll = async () => {
      if (!activePollingIds.value.has(jobId)) {
        clearPollingTimer(jobId)
        return
      }

      try {
        const job = await fetchStatus(jobId)

        if (TERMINAL_STATUSES.includes(job.status)) {
          activePollingIds.value.delete(jobId)
          clearPollingTimer(jobId)
          pollingState.value[jobId] = 'completed'
          onComplete?.(job)
          return
        }

        intervalMs = Math.min(intervalMs * 1.5, maxIntervalMs)
        pollingTimers.value[jobId] = setTimeout(poll, intervalMs)
      } catch (err: unknown) {
        activePollingIds.value.delete(jobId)
        clearPollingTimer(jobId)
        pollingState.value[jobId] = 'error'
        pollingError.value[jobId] = (err as { message?: string })?.message ?? 'Failed to poll recommendations status.'
      }
    }

    pollingTimers.value[jobId] = setTimeout(poll, intervalMs)
  }

  function stopPolling(jobId: string) {
    activePollingIds.value.delete(jobId)
    clearPollingTimer(jobId)
    pollingState.value[jobId] = 'stopped'
  }

  function getPollingState(jobId: string) {
    return pollingState.value[jobId] ?? 'stopped'
  }

  function getPollingError(jobId: string) {
    return pollingError.value[jobId] ?? null
  }

  async function submitFeedback(request: SubmitRecommendationFeedbackRequest): Promise<void> {
    await api.post<void>('/recommendations/feedback', request)
  }

  return {
    jobs,
    activePollingIds,
    getJob,
    fetchStatus,
    create,
    startPolling,
    stopPolling,
    getPollingState,
    getPollingError,
    submitFeedback
  }
})
