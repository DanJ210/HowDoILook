<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import type { UserJobSummaryResponse, JobStatus, UpdateJobVisibilityRequest } from '@/types/api'
import { useBackendRequestState } from '@/composables/useBackendRequestState'
import { getDarkJobStatusPillClass } from '@/constants/jobStatusStyles'
import StateCard from '@/components/StateCard.vue'

const router = useRouter()
const authStore = useAuthStore()
const requestState = useBackendRequestState({
  retryDelayMs: 3000,
  offlineMessage: 'Waiting for backend to come online…'
})
const { isLoading, isWaitingForBackend, error, offlineMessage } = requestState

const jobs = ref<UserJobSummaryResponse[]>([])
const savingVisibility = ref<Record<string, boolean>>({})

function pillClass(status: JobStatus) {
  return getDarkJobStatusPillClass(status)
}

async function fetchJobs(isRetry = false) {
  if (!authStore.isAuthenticated) return

  if (!isRetry) {
    requestState.beginInitialLoad()
  }
  try {
    jobs.value = await api.get<UserJobSummaryResponse[]>('/jobs')
    requestState.reset()
  } catch (err: unknown) {
    requestState.handleError(err, () => fetchJobs(true))
  } finally {
    requestState.finishLoad()
  }
}

onMounted(fetchJobs)

function openJob(jobId: string) {
  router.push({ name: 'job-status', params: { id: jobId } })
}

async function toggleVisibility(job: UserJobSummaryResponse) {
  const nextVisibility = !job.isResultPublic
  const previousVisibility = job.isResultPublic

  savingVisibility.value = { ...savingVisibility.value, [job.jobId]: true }
  job.isResultPublic = nextVisibility

  try {
    const updated = await api.put<UserJobSummaryResponse>(
      `/jobs/${job.jobId}/visibility`,
      { isResultPublic: nextVisibility } satisfies UpdateJobVisibilityRequest
    )

    job.isResultPublic = updated.isResultPublic
  } catch {
    job.isResultPublic = previousVisibility
  } finally {
    savingVisibility.value = { ...savingVisibility.value, [job.jobId]: false }
  }
}

const hasJobs = computed(() => jobs.value.length > 0)
</script>

<template>
  <main class="mx-auto max-w-5xl px-4 pt-6 pb-8 sm:pt-10">
    <div class="mb-6 flex items-end justify-between gap-4">
      <div>
        <p class="text-xs uppercase tracking-[0.28em] text-sky-200/70">Your activity</p>
        <h1 class="mt-2 text-3xl font-semibold text-white">Generated jobs</h1>
      </div>
      <button
        type="button"
        @click="router.push({ name: 'style-generate' })"
        class="hidden sm:inline-flex rounded-full border border-white/10 bg-white/5 px-4 py-2 text-sm font-medium text-slate-200 transition hover:bg-white/10"
      >
        New generation
      </button>
    </div>

    <StateCard
      v-if="!authStore.isAuthenticated"
      title="Sign in to view your jobs."
      description="Use the top-right auth controls to sign in."
    />

    <div v-else-if="isLoading || isWaitingForBackend" class="space-y-3 py-24 text-center text-slate-300">
      <div class="mx-auto grid max-w-3xl grid-cols-1 gap-4 sm:grid-cols-2">
        <div v-for="n in 4" :key="n" class="h-32 animate-pulse rounded-3xl bg-white/5" />
      </div>
      <p v-if="isWaitingForBackend" class="text-sm text-slate-400">
        {{ offlineMessage }} retrying every 3s
      </p>
      <p v-else class="animate-pulse text-slate-300">Loading your jobs…</p>
    </div>

    <StateCard
      v-else-if="error"
      tone="error"
      :title="error"
      padding-class="p-4"
      :centered="false"
    />

    <StateCard
      v-else-if="!hasJobs"
      title="No jobs yet."
      description="Create your first look from the Generate tab."
    />

    <section v-else class="grid gap-4">
      <article
        v-for="job in jobs"
        :key="job.jobId"
        class="overflow-hidden rounded-[1.75rem] border border-white/10 bg-white/5 shadow-xl shadow-black/10 backdrop-blur"
      >
        <div class="grid gap-0 sm:grid-cols-[160px_1fr]">
          <div class="relative aspect-[4/5] bg-slate-900/60 sm:aspect-auto">
            <img
              v-if="job.resultImageUrl"
              :src="job.resultImageUrl"
              :alt="job.styleName"
              class="h-full w-full object-cover"
            />
            <div v-else class="flex h-full w-full items-center justify-center text-xs uppercase tracking-[0.28em] text-slate-500">
              No preview
            </div>
          </div>

          <div class="flex flex-col justify-between gap-4 p-4 sm:p-5">
            <div class="space-y-3">
              <div class="flex flex-wrap items-center gap-2">
                <span :class="['inline-flex items-center rounded-full border px-3 py-1 text-xs font-medium', pillClass(job.status)]">
                  {{ job.status }}
                </span>
                <span
                  v-if="job.isResultPublic"
                  class="inline-flex items-center rounded-full border border-sky-400/20 bg-sky-500/10 px-3 py-1 text-xs font-medium text-sky-100"
                >
                  Public
                </span>
              </div>

              <div>
                <h2 class="text-xl font-semibold text-white">{{ job.styleName }}</h2>
                <p class="mt-1 text-sm text-slate-300">
                  Job created {{ new Date(job.createdAtUtc).toLocaleString() }}
                </p>
              </div>
            </div>

            <div class="flex flex-wrap items-center justify-between gap-3">
              <div class="flex items-center gap-2">
                <span class="text-xs text-slate-400">
                  {{ job.isResultPublic ? 'Public result' : 'Private result' }}
                </span>
                <button
                  type="button"
                  role="switch"
                  :aria-checked="job.isResultPublic"
                  :disabled="savingVisibility[job.jobId]"
                  @click="toggleVisibility(job)"
                  :class="[
                    'relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400 disabled:cursor-not-allowed disabled:opacity-60',
                    job.isResultPublic ? 'bg-sky-500' : 'bg-slate-600'
                  ]"
                >
                  <span
                    :class="[
                      'pointer-events-none inline-block h-5 w-5 rounded-full bg-white shadow transform transition duration-200',
                      job.isResultPublic ? 'translate-x-5' : 'translate-x-0'
                    ]"
                  />
                </button>
              </div>

              <div class="flex items-center gap-3">
                <p class="text-xs text-slate-400">
                  {{ job.completedAtUtc ? `Completed ${new Date(job.completedAtUtc).toLocaleString()}` : 'Still in progress' }}
                </p>
                <button
                  type="button"
                  @click="openJob(job.jobId)"
                  class="rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-slate-100 transition hover:bg-white/20"
                >
                  Open
                </button>
              </div>
            </div>
          </div>
        </div>
      </article>
    </section>
  </main>
</template>
