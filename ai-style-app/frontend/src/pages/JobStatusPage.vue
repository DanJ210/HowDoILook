<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useJobStore } from '@/stores/job'
import { TERMINAL_STATUSES } from '@/types/api'

const route = useRoute()
const router = useRouter()
const jobStore = useJobStore()

const jobId = computed(() => route.params.id as string)
const job = computed(() => jobStore.getJob(jobId.value))
const isPolling = computed(() => jobStore.activePollingIds.has(jobId.value))

onMounted(() => {
  if (jobId.value) {
    jobStore.fetchJob(jobId.value)
    if (!job.value || !TERMINAL_STATUSES.includes(job.value.status)) {
      jobStore.startPolling(jobId.value)
    }
  }
})

const statusColors: Record<string, string> = {
  Queued:     'bg-yellow-100 text-yellow-800',
  Processing: 'bg-blue-100 text-blue-800',
  Succeeded:  'bg-green-100 text-green-800',
  Failed:     'bg-red-100 text-red-800',
  TimedOut:   'bg-gray-100 text-gray-800',
  Canceled:   'bg-gray-100 text-gray-800',
}

function statusColor(status: string) {
  return statusColors[status] ?? 'bg-gray-100 text-gray-800'
}

function parsedResult(json: string | null) {
  if (!json) return null
  try { return JSON.parse(json) } catch { return json }
}

function goBack() {
  if (window.history.length > 1) {
    router.back()
    return
  }

  router.push({ name: 'home' })
}
</script>

<template>
  <main class="max-w-xl mx-auto px-4 py-8">
    <div class="mb-6 flex items-center justify-between gap-4">
      <h1 class="text-2xl font-bold">Job Status</h1>
      <button
        @click="goBack"
        class="bg-slate-100 text-slate-700 px-3 py-1.5 rounded hover:bg-slate-200 transition text-sm"
      >
        Back to List
      </button>
    </div>

    <div v-if="!job" class="text-gray-500">
      <span v-if="isPolling">Loading job…</span>
      <span v-else>Job not found.</span>
    </div>

    <div v-else class="space-y-4">
      <div class="flex items-center gap-3">
        <span
          :class="['px-3 py-1 rounded-full text-sm font-medium', statusColor(job.status)]"
        >
          {{ job.status }}
        </span>
        <span v-if="isPolling" class="text-sm text-gray-400 animate-pulse">polling…</span>
      </div>

      <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <dt class="text-gray-500 font-medium">Job ID</dt>
        <dd class="font-mono truncate">{{ job.id }}</dd>

        <dt class="text-gray-500 font-medium">Style Item</dt>
        <dd class="font-mono truncate">{{ job.styleItemId }}</dd>

        <dt class="text-gray-500 font-medium">Attempts</dt>
        <dd>{{ job.attemptCount }}</dd>

        <dt class="text-gray-500 font-medium">Created</dt>
        <dd>{{ new Date(job.createdAtUtc).toLocaleString() }}</dd>

        <template v-if="job.startedAtUtc">
          <dt class="text-gray-500 font-medium">Started</dt>
          <dd>{{ new Date(job.startedAtUtc).toLocaleString() }}</dd>
        </template>

        <template v-if="job.completedAtUtc">
          <dt class="text-gray-500 font-medium">Completed</dt>
          <dd>{{ new Date(job.completedAtUtc).toLocaleString() }}</dd>
        </template>
      </dl>

      <div v-if="job.errorMessage" class="p-3 bg-red-50 border border-red-200 rounded text-sm text-red-700">
        <strong>Error:</strong> {{ job.errorMessage }}
      </div>

      <div v-if="job.resultJson" class="space-y-2">
        <h2 class="font-semibold">Result</h2>
        <pre class="bg-gray-50 border rounded p-3 text-xs overflow-auto max-h-64">{{ parsedResult(job.resultJson) }}</pre>
      </div>
    </div>
  </main>
</template>
