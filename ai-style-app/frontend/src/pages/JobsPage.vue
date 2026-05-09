<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import type { UserJobSummaryResponse, JobStatus } from '@/types/api'

const router = useRouter()
const authStore = useAuthStore()

const jobs = ref<UserJobSummaryResponse[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

const statusPillClass: Record<JobStatus, string> = {
  Queued: 'bg-amber-500/15 text-amber-200 border-amber-400/20',
  Processing: 'bg-sky-500/15 text-sky-200 border-sky-400/20',
  Succeeded: 'bg-emerald-500/15 text-emerald-200 border-emerald-400/20',
  Failed: 'bg-rose-500/15 text-rose-200 border-rose-400/20',
  TimedOut: 'bg-slate-500/15 text-slate-200 border-slate-400/20',
  Canceled: 'bg-slate-500/15 text-slate-200 border-slate-400/20'
}

function pillClass(status: JobStatus) {
  return statusPillClass[status] ?? 'bg-slate-500/15 text-slate-200 border-slate-400/20'
}

async function fetchJobs() {
  if (!authStore.isAuthenticated) return

  isLoading.value = true
  error.value = null
  try {
    jobs.value = await api.get<UserJobSummaryResponse[]>('/jobs')
  } catch (err: unknown) {
    error.value = (err as { message?: string })?.message ?? 'Failed to load your jobs.'
  } finally {
    isLoading.value = false
  }
}

async function handleDevLogin() {
  await authStore.loginDev('dev-user')
  await fetchJobs()
}

onMounted(fetchJobs)

function openJob(jobId: string) {
  router.push({ name: 'job-status', params: { id: jobId } })
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

    <div v-if="!authStore.isAuthenticated" class="rounded-3xl border border-white/10 bg-white/5 p-6 text-center text-slate-200 shadow-xl shadow-black/10">
      <p class="text-lg font-medium">Sign in to view your jobs.</p>
      <p class="mt-2 text-sm text-slate-400">Use the dev login to continue from local development.</p>
      <button
        type="button"
        @click="handleDevLogin"
        class="mt-4 w-full rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-white transition hover:bg-sky-400 sm:w-auto"
      >
        Dev Login
      </button>
    </div>

    <div v-else-if="isLoading" class="py-24 text-center text-slate-300 animate-pulse">Loading your jobs…</div>

    <div v-else-if="error" class="rounded-3xl border border-rose-400/20 bg-rose-500/10 p-4 text-rose-100">
      {{ error }}
    </div>

    <div v-else-if="!hasJobs" class="rounded-3xl border border-white/10 bg-white/5 p-6 text-center text-slate-200 shadow-xl shadow-black/10">
      <p class="text-lg font-medium">No jobs yet.</p>
      <p class="mt-2 text-sm text-slate-400">Create your first look from the Generate tab.</p>
    </div>

    <section v-else class="grid gap-4">
      <article
        v-for="job in jobs"
        :key="job.jobId"
        class="overflow-hidden rounded-[1.75rem] border border-white/10 bg-white/5 shadow-xl shadow-black/10 backdrop-blur"
      >
        <button
          type="button"
          class="block w-full text-left"
          @click="openJob(job.jobId)"
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

              <div class="flex items-center justify-between gap-3">
                <p class="text-xs text-slate-400">
                  {{ job.completedAtUtc ? `Completed ${new Date(job.completedAtUtc).toLocaleString()}` : 'Still in progress' }}
                </p>
                <span class="rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-slate-100 transition">
                  Open
                </span>
              </div>
            </div>
          </div>
        </button>
      </article>
    </section>
  </main>
</template>
