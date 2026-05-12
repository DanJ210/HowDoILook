import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api/client'
import type { JobStatusResponse } from '@/types/api'
import { TERMINAL_STATUSES } from '@/types/api'

export const useJobStore = defineStore('job', () => {
  const jobs = ref<Record<string, JobStatusResponse>>({})
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

  function getJob(jobId: string): JobStatusResponse | undefined {
    return jobs.value[jobId]
  }

  async function fetchJob(jobId: string): Promise<JobStatusResponse> {
    const job = await api.get<JobStatusResponse>(`/jobs/${jobId}`)
    jobs.value[jobId] = job
    return job
  }

  function startPolling(jobId: string, onComplete?: (job: JobStatusResponse) => void) {
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
        const job = await fetchJob(jobId)

        if (TERMINAL_STATUSES.includes(job.status)) {
          activePollingIds.value.delete(jobId)
          clearPollingTimer(jobId)
          pollingState.value[jobId] = 'completed'
          onComplete?.(job)
          return
        }

        // Exponential backoff — cap at 10s
        intervalMs = Math.min(intervalMs * 1.5, maxIntervalMs)
        pollingTimers.value[jobId] = setTimeout(poll, intervalMs)
      } catch (err: unknown) {
        activePollingIds.value.delete(jobId)
        clearPollingTimer(jobId)
        pollingState.value[jobId] = 'error'
        pollingError.value[jobId] = (err as { message?: string })?.message ?? 'Failed to poll job status.'
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

  return { jobs, activePollingIds, getJob, fetchJob, startPolling, stopPolling, getPollingState, getPollingError }
})
