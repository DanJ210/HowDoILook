import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api/client'
import type { JobStatusResponse } from '@/types/api'
import { TERMINAL_STATUSES } from '@/types/api'

export const useJobStore = defineStore('job', () => {
  const jobs = ref<Record<string, JobStatusResponse>>({})
  const activePollingIds = ref<Set<string>>(new Set())

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

    let intervalMs = 2000
    const maxIntervalMs = 10000

    const poll = async () => {
      try {
        const job = await fetchJob(jobId)

        if (TERMINAL_STATUSES.includes(job.status)) {
          activePollingIds.value.delete(jobId)
          onComplete?.(job)
          return
        }

        // Exponential backoff — cap at 10s
        intervalMs = Math.min(intervalMs * 1.5, maxIntervalMs)
        setTimeout(poll, intervalMs)
      } catch {
        activePollingIds.value.delete(jobId)
      }
    }

    setTimeout(poll, intervalMs)
  }

  function stopPolling(jobId: string) {
    activePollingIds.value.delete(jobId)
  }

  return { jobs, activePollingIds, getJob, fetchJob, startPolling, stopPolling }
})
