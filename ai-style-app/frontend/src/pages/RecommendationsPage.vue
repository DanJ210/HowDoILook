<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useRecommendationsStore } from '@/stores/recommendations'
import { useImageFileInput } from '@/composables/useImageFileInput'
import StateCard from '@/components/StateCard.vue'
import { computeWorkflowStateLabel, hasGenerationFailed } from '@/utils/recommendationWorkflow'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()
const recommendationsStore = useRecommendationsStore()
const { selectedFile, previewUrl, onFileChange: onFileChangeFromInput, onDrop: onDropFromInput, removeFile } = useImageFileInput()

const LAST_ANALYSIS_JOB_KEY = 'recommendations:lastAnalysisJobId'

const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const activeJobId = ref<string | null>(null)
const recommendationPostId = ref<string | null>(null)
const publicEndpoint = ref<string | null>(null)

const feedbackComment = ref('')
const feedbackTags = ref<string[]>([])
const variantRanks = ref<Record<string, 1 | 2 | 3 | null>>({})
const feedbackMessage = ref<string | null>(null)
const feedbackError = ref<string | null>(null)
const isSubmittingFeedback = ref(false)

const feedbackTagOptions = [
  { value: 'tooBold', label: 'Too bold' },
  { value: 'notMyStyle', label: 'Not my style' },
  { value: 'tooHighMaintenance', label: 'Too high maintenance' },
  { value: 'greatFit', label: 'Great fit' }
] as const

const activeJob = computed(() => (activeJobId.value ? recommendationsStore.getJob(activeJobId.value) : undefined))
const pollingState = computed(() => (activeJobId.value ? recommendationsStore.getPollingState(activeJobId.value) : 'stopped'))
const pollingError = computed(() => (activeJobId.value ? recommendationsStore.getPollingError(activeJobId.value) : null))

const bestRecommendation = computed(() => activeJob.value?.bestRecommendation ?? null)
const bestVariant = computed(() => activeJob.value?.bestVariant ?? null)
const experimentalVariants = computed(() => activeJob.value?.experimentalVariants ?? [])
const succeededExperimentalVariants = computed(() => experimentalVariants.value.filter(variant => variant.status === 'Succeeded'))
const rankedRecommendations = computed(() => activeJob.value?.recommendations ?? [])
const debugTelemetry = computed(() => activeJob.value?.debugTelemetry ?? null)
const hasGenerationFailure = computed(() => hasGenerationFailed({
  hasActiveJob: Boolean(activeJob.value),
  analysisStatus: activeJob.value?.status ?? null,
  bestVariant: bestVariant.value,
  experimentalVariants: experimentalVariants.value
}))
const workflowStateLabel = computed(() => {
  return computeWorkflowStateLabel({
    hasActiveJob: Boolean(activeJob.value),
    analysisStatus: activeJob.value?.status ?? null,
    bestVariant: bestVariant.value,
    experimentalVariants: experimentalVariants.value
  })
})
const analysisSucceeded = computed(() => activeJob.value?.status === 'Succeeded')
const hasFailed = computed(() => workflowStateLabel.value === 'failed')
const hasCompleted = computed(() => workflowStateLabel.value === 'completed')
const generationFailureMessage = computed(() => {
  if (!hasGenerationFailure.value) {
    return null
  }

  return activeJob.value?.errorMessage
    ?? 'The automatic primary result could not be generated. Try another photo or run Analyze and Recommend again.'
})
const resolvedPublicEndpoint = computed(() => {
  if (publicEndpoint.value) {
    return publicEndpoint.value
  }

  const postId = activeJob.value?.recommendationPostId ?? recommendationPostId.value
  return postId ? `/api/style/${postId}` : null
})

function getRouteJobId(): string | null {
  const value = route.query.jobId
  return typeof value === 'string' && value.length > 0 ? value : null
}

function persistJobId(jobId: string | null) {
  if (!jobId) {
    localStorage.removeItem(LAST_ANALYSIS_JOB_KEY)
    return
  }

  localStorage.setItem(LAST_ANALYSIS_JOB_KEY, jobId)
}

async function syncRouteJobId(jobId: string) {
  if (route.query.jobId === jobId) {
    return
  }

  try {
    await router.replace({
      query: {
        ...route.query,
        jobId
      }
    })
  } catch {
    // Ignore navigation duplication and non-critical route update errors.
  }
}

async function restoreActiveJob() {
  if (!authStore.isAuthenticated) {
    return
  }

  const routeJobId = getRouteJobId()
  const storedJobId = localStorage.getItem(LAST_ANALYSIS_JOB_KEY)
  const jobIdToRestore = routeJobId ?? storedJobId

  if (!jobIdToRestore) {
    return
  }

  activeJobId.value = jobIdToRestore
  persistJobId(jobIdToRestore)
  await syncRouteJobId(jobIdToRestore)

  try {
    const status = await recommendationsStore.fetchStatus(jobIdToRestore)
    recommendationPostId.value = status.recommendationPostId ?? null
    publicEndpoint.value = status.recommendationPostId ? `/api/style/${status.recommendationPostId}` : null
    recommendationsStore.startPolling(jobIdToRestore)
  } catch {
    persistJobId(null)
    activeJobId.value = null

    try {
      const nextQuery = { ...route.query }
      delete nextQuery.jobId
      await router.replace({ query: nextQuery })
    } catch {
      // Ignore navigation cleanup errors.
    }
  }
}

function onFileChange(event: Event) {
  submitError.value = null
  const fileError = onFileChangeFromInput(event)
  if (fileError) {
    submitError.value = fileError
  }
}

function onDrop(event: DragEvent) {
  submitError.value = null
  const fileError = onDropFromInput(event)
  if (fileError) {
    submitError.value = fileError
  }
}

function resetFeedbackState() {
  feedbackComment.value = ''
  feedbackTags.value = []
  variantRanks.value = {}
  feedbackMessage.value = null
  feedbackError.value = null
  isSubmittingFeedback.value = false
}

async function startRecommendation() {
  submitError.value = null
  feedbackMessage.value = null
  feedbackError.value = null

  if (!selectedFile.value) {
    submitError.value = 'Upload a photo to get recommendations.'
    return
  }

  isSubmitting.value = true

  try {
    const created = await recommendationsStore.create({}, selectedFile.value)

    activeJobId.value = created.analysisJobId
    recommendationPostId.value = created.recommendationPostId
    publicEndpoint.value = created.publicEndpoint
    persistJobId(created.analysisJobId)
    await syncRouteJobId(created.analysisJobId)
    resetFeedbackState()

    recommendationsStore.startPolling(created.analysisJobId)
  } catch (err: unknown) {
    submitError.value = (err as { message?: string })?.message ?? 'Failed to create recommendation job.'
  } finally {
    isSubmitting.value = false
  }
}

async function submitRatings() {
  if (!activeJobId.value) return

  const rankings = Object.entries(variantRanks.value)
    .filter(([, rank]) => rank !== null)
    .map(([generationJobId, rank]) => ({
      generationJobId,
      rank: rank as 1 | 2 | 3
    }))

  if (rankings.length === 0) {
    feedbackError.value = 'Choose at least one experimental variant rank before submitting.'
    return
  }

  isSubmittingFeedback.value = true
  feedbackMessage.value = null
  feedbackError.value = null

  try {
    await recommendationsStore.submitRatings(activeJobId.value, {
      analysisJobId: activeJobId.value,
      rankings,
      feedbackTags: feedbackTags.value.length ? feedbackTags.value : null,
      comment: feedbackComment.value.trim() ? feedbackComment.value.trim() : null
    })

    feedbackMessage.value = 'Thanks, your rankings were submitted.'
  } catch (err: unknown) {
    feedbackError.value = (err as { message?: string })?.message ?? 'Failed to submit rankings.'
  } finally {
    isSubmittingFeedback.value = false
  }
}

function setVariantRank(generationJobId: string, rank: 1 | 2 | 3 | null) {
  variantRanks.value = {
    ...variantRanks.value,
    [generationJobId]: rank
  }
}

function onVariantRankChange(generationJobId: string, event: Event) {
  const value = (event.target as HTMLSelectElement).value
  const rank = value === '' ? null : Number(value)
  if (rank === null || rank === 1 || rank === 2 || rank === 3) {
    setVariantRank(generationJobId, rank)
  }
}

function getVariantRankValue(generationJobId: string, selectedRank: string | null) {
  const explicit = variantRanks.value[generationJobId]
  if (explicit !== undefined && explicit !== null) {
    return String(explicit)
  }

  return selectedRank ?? ''
}

function toggleTag(tag: string) {
  if (feedbackTags.value.includes(tag)) {
    feedbackTags.value = feedbackTags.value.filter(t => t !== tag)
    return
  }

  feedbackTags.value = [...feedbackTags.value, tag]
}

onUnmounted(() => {
  if (activeJobId.value) {
    recommendationsStore.stopPolling(activeJobId.value)
  }
})

onMounted(async () => {
  await restoreActiveJob()
})
</script>

<template>
  <main class="mx-auto max-w-4xl px-4 py-6 sm:py-8">
    <h1 class="mb-2 text-2xl font-bold text-white">How do I look?</h1>
    <p class="mb-6 text-sm text-slate-300">
      Upload one portrait to receive an automatically selected best-look recommendation.
    </p>

    <StateCard
      v-if="!authStore.isAuthenticated"
      title="Sign in to get recommendations."
      description="Use the top-right auth controls to sign in."
      :centered="false"
      padding-class="p-6"
    />

    <div v-else class="grid gap-6 lg:grid-cols-[0.9fr_1.1fr]">
      <section class="rounded-3xl border border-white/10 bg-white/5 p-5">
        <h2 class="text-lg font-semibold text-white">Your portrait</h2>

        <div class="mt-4 space-y-4">
          <div>
            <label class="mb-1 block text-sm font-medium">Portrait photo</label>

            <div
              v-if="!previewUrl"
              class="cursor-pointer rounded-xl border-2 border-dashed border-white/20 p-5 text-center transition hover:border-sky-400"
              @click="($refs.fileInput as HTMLInputElement).click()"
              @dragover.prevent
              @drop="onDrop"
            >
              <p class="text-sm text-slate-300">Drag and drop or <span class="text-sky-300 underline">browse</span></p>
              <p class="mt-1 text-xs text-slate-400">JPEG, PNG, WebP, GIF up to 10MB</p>
              <input
                ref="fileInput"
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                class="hidden"
                @change="onFileChange"
              />
            </div>

            <div v-else class="relative overflow-hidden rounded-xl border border-white/10">
              <img :src="previewUrl" alt="Preview" class="h-56 w-full object-cover" />
              <button
                type="button"
                @click="removeFile"
                class="absolute right-2 top-2 rounded-full bg-black/70 px-2 py-1 text-xs text-white hover:bg-black/90"
              >
                Remove
              </button>
            </div>
          </div>

          <div v-if="submitError" class="rounded-xl border border-rose-400/20 bg-rose-500/10 px-3 py-2 text-sm text-rose-100">
            {{ submitError }}
          </div>

          <button
            type="button"
            :disabled="isSubmitting"
            @click="startRecommendation"
            class="w-full rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-white transition hover:bg-sky-400 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {{ isSubmitting ? 'Submitting…' : 'Analyze and Recommend' }}
          </button>
        </div>
      </section>

      <section class="rounded-3xl border border-white/10 bg-white/5 p-5">
        <h2 class="text-lg font-semibold text-white">Your automatic result</h2>

        <StateCard
          v-if="!activeJobId"
          title="No active recommendation job"
          description="Submit a photo to begin analysis."
          padding-class="p-5"
        />

        <div v-else-if="activeJob" class="mt-4 space-y-4">
          <div class="flex flex-wrap items-center gap-2">
            <span
              :class="[
                'rounded-full px-3 py-1 text-sm font-medium capitalize',
                hasFailed
                  ? 'bg-rose-500/20 text-rose-100'
                  : hasCompleted
                    ? 'bg-emerald-500/20 text-emerald-100'
                    : 'bg-sky-500/20 text-sky-100'
              ]"
            >
              {{ workflowStateLabel }}
            </span>
            <span v-if="pollingState === 'polling'" class="text-sm text-slate-400">Checking for updates…</span>
            <span v-else-if="pollingState === 'error'" class="text-sm text-rose-300">Updates paused</span>
          </div>

          <div v-if="pollingError" class="rounded-xl border border-amber-400/20 bg-amber-500/10 px-3 py-2 text-sm text-amber-200">
            {{ pollingError }}
          </div>

          <div v-if="hasFailed" class="rounded-xl border border-rose-400/20 bg-rose-500/10 px-3 py-2 text-sm text-rose-100">
            <p class="font-medium">
              {{ activeJob.errorCode ?? activeJob.qualityGate.failureCode ?? (hasGenerationFailure ? 'PRIMARY_GENERATION_FAILED' : 'ANALYSIS_FAILED') }}
            </p>
            <p class="mt-1">
              {{ activeJob.errorMessage ?? activeJob.qualityGate.message ?? generationFailureMessage ?? 'Try another portrait.' }}
            </p>
          </div>

          <div v-if="analysisSucceeded" class="space-y-3">
            <h3 class="text-base font-semibold">Best recommendation</h3>

            <StateCard
              v-if="!bestRecommendation"
              title="No best recommendation produced"
              description="Try another portrait."
              padding-class="p-4"
            />

            <article
              v-else
              class="rounded-2xl border border-white/10 bg-slate-900/50 p-4"
            >
              <div class="mb-2 flex items-center justify-between gap-4">
                <h4 class="text-sm font-semibold text-white">{{ bestRecommendation.styleName }}</h4>
                <span class="rounded-full bg-sky-500/20 px-2 py-1 text-xs font-medium text-sky-200">
                  {{ bestRecommendation.score.toFixed(3) }}
                </span>
              </div>

              <p class="mb-1 text-xs uppercase tracking-wide text-slate-400">Reasons</p>
              <ul class="list-disc space-y-1 pl-5 text-sm text-slate-200">
                <li v-for="reason in bestRecommendation.reasons" :key="reason">{{ reason }}</li>
              </ul>

              <p class="mb-1 mt-3 text-xs uppercase tracking-wide text-slate-400">Constraints</p>
              <ul class="list-disc space-y-1 pl-5 text-sm text-slate-200">
                <li v-for="constraint in bestRecommendation.constraints" :key="constraint">{{ constraint }}</li>
              </ul>
            </article>

            <div class="rounded-2xl border border-white/10 bg-slate-900/50 p-4">
              <h3 class="text-sm font-semibold">Generated preview</h3>
              <div v-if="bestVariant" class="mt-2 text-sm text-slate-200">
                <img
                  v-if="bestVariant.resultImageUrl"
                  :src="bestVariant.resultImageUrl"
                  alt="Best generated recommendation"
                  class="max-h-72 w-full rounded-xl object-cover"
                />
                <p v-else>Generation status: {{ bestVariant.status }}</p>
              </div>
              <p v-else class="mt-2 text-sm text-slate-400">Preparing your automatic result…</p>
            </div>

            <details v-if="hasCompleted && succeededExperimentalVariants.length > 0" class="rounded-2xl border border-white/10 bg-slate-900/50 p-4">
              <summary class="cursor-pointer text-sm font-semibold text-white">Optional comparison</summary>
              <p class="mt-2 text-xs text-slate-400">Compare experimental looks and rank any you prefer.</p>

              <div class="mt-3 space-y-3">
                <article
                  v-for="variant in succeededExperimentalVariants"
                  :key="variant.generationJobId"
                  class="rounded-xl border border-white/10 bg-slate-950/40 p-3"
                >
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <p class="text-sm font-medium text-white">Variant {{ variant.slot }}</p>
                    <span class="text-xs text-slate-300">{{ variant.status }}</span>
                  </div>
                  <p class="mt-1 font-mono text-xs text-slate-400">{{ variant.generationJobId }}</p>
                  <img
                    v-if="variant.resultImageUrl"
                    :src="variant.resultImageUrl"
                    :alt="`Experimental variant ${variant.slot}`"
                    class="mt-2 max-h-56 w-full rounded-xl object-cover"
                  />
                  <div class="mt-3">
                    <label class="mb-1 block text-xs uppercase tracking-wide text-slate-400">Rank</label>
                    <select
                      :value="getVariantRankValue(variant.generationJobId, variant.selectedRank)"
                      @change="onVariantRankChange(variant.generationJobId, $event)"
                      class="w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-sm text-white"
                    >
                      <option :value="''">Unranked</option>
                      <option :value="1">1</option>
                      <option :value="2">2</option>
                      <option :value="3">3</option>
                    </select>
                  </div>
                </article>
              </div>
            </details>

            <details class="rounded-2xl border border-white/10 bg-slate-900/40 p-4">
              <summary class="cursor-pointer text-sm font-semibold text-white">Technical details</summary>

              <div class="mt-3 space-y-3">
                <dl class="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
                  <dt class="text-slate-400">Job ID</dt>
                  <dd class="break-all font-mono text-slate-200 sm:text-right">{{ activeJob.analysisJobId }}</dd>
                  <dt class="text-slate-400">Recommendation post</dt>
                  <dd class="break-all font-mono text-slate-200 sm:text-right">{{ activeJob.recommendationPostId ?? recommendationPostId ?? 'Pending' }}</dd>
                  <dt class="text-slate-400">Publish status</dt>
                  <dd class="sm:text-right">{{ activeJob.publishStatus ?? 'Pending' }}</dd>
                  <dt class="text-slate-400">Quality gate</dt>
                  <dd class="sm:text-right">{{ activeJob.qualityGate.passed === null ? 'Pending' : activeJob.qualityGate.passed ? 'Passed' : 'Failed' }}</dd>
                  <dt class="text-slate-400">Face shape</dt>
                  <dd class="sm:text-right">{{ activeJob.analysisSummary.faceShape ?? 'Pending' }}</dd>
                  <dt class="text-slate-400">Confidence</dt>
                  <dd class="sm:text-right">{{ activeJob.analysisSummary.confidence === null ? 'Pending' : activeJob.analysisSummary.confidence.toFixed(3) }}</dd>
                  <dt v-if="resolvedPublicEndpoint" class="text-slate-400">Public endpoint</dt>
                  <dd v-if="resolvedPublicEndpoint" class="break-all font-mono text-slate-200 sm:text-right">{{ resolvedPublicEndpoint }}</dd>
                </dl>

                <div v-if="debugTelemetry" class="rounded-2xl border border-white/10 bg-slate-900/50 p-3">
                  <h3 class="text-sm font-semibold text-white">Stage telemetry</h3>
                  <p class="mt-1 text-xs text-slate-300">
                    Source: <span class="font-mono">{{ debugTelemetry.source ?? 'unknown' }}</span>
                  </p>
                  <p class="mt-1 text-xs text-slate-300">
                    Dimensions:
                    {{ debugTelemetry.imageWidth && debugTelemetry.imageHeight
                      ? `${debugTelemetry.imageWidth} x ${debugTelemetry.imageHeight}`
                      : 'unknown' }}
                  </p>
                  <div class="mt-2 space-y-2" v-if="debugTelemetry.stages.length">
                    <article v-for="stage in debugTelemetry.stages" :key="stage.stage" class="rounded-xl border border-white/10 bg-slate-950/40 p-3">
                      <div class="flex flex-wrap items-center justify-between gap-2">
                        <p class="text-sm font-medium text-white">{{ stage.stage }}</p>
                        <p class="text-xs text-slate-300">{{ stage.durationMs.toFixed(2) }} ms</p>
                      </div>
                      <p class="mt-1 text-xs text-slate-400">{{ stage.model }} ({{ stage.modelVersion }})</p>
                      <p v-if="stage.notes" class="mt-1 break-all text-xs text-amber-200">{{ stage.notes }}</p>
                      <div v-if="stage.metrics" class="mt-2 grid grid-cols-1 gap-1 text-xs text-slate-300 sm:grid-cols-2">
                        <div v-for="(metricValue, metricName) in stage.metrics" :key="metricName" class="flex items-center justify-between gap-2 rounded bg-white/5 px-2 py-1">
                          <span>{{ metricName }}</span>
                          <span class="font-mono">{{ metricValue.toFixed(4) }}</span>
                        </div>
                      </div>
                    </article>
                  </div>
                </div>

                <h3 class="text-base font-semibold">Ranked candidates</h3>

                <StateCard
                  v-if="rankedRecommendations.length === 0"
                  title="No recommendations produced"
                  description="Try another portrait."
                  padding-class="p-4"
                />

                <article
                  v-for="item in rankedRecommendations"
                  :key="item.styleId"
                  class="rounded-2xl border border-white/10 bg-slate-900/50 p-4"
                >
                  <div class="mb-2 flex items-center justify-between gap-4">
                    <h4 class="text-sm font-semibold text-white">{{ item.styleName }}</h4>
                    <span class="rounded-full bg-sky-500/20 px-2 py-1 text-xs font-medium text-sky-200">
                      {{ item.score.toFixed(3) }}
                    </span>
                  </div>

                  <p class="mb-1 text-xs uppercase tracking-wide text-slate-400">Reasons</p>
                  <ul class="list-disc space-y-1 pl-5 text-sm text-slate-200">
                    <li v-for="reason in item.reasons" :key="reason">{{ reason }}</li>
                  </ul>

                  <p class="mb-1 mt-3 text-xs uppercase tracking-wide text-slate-400">Constraints</p>
                  <ul class="list-disc space-y-1 pl-5 text-sm text-slate-200">
                    <li v-for="constraint in item.constraints" :key="constraint">{{ constraint }}</li>
                  </ul>
                </article>
              </div>
            </details>

            <details v-if="hasCompleted && succeededExperimentalVariants.length > 0" class="rounded-2xl border border-white/10 bg-slate-900/50 p-4">
              <summary class="cursor-pointer text-sm font-semibold text-white">Optional feedback</summary>

              <div class="mt-3">
                <p class="mb-1 text-xs uppercase tracking-wide text-slate-400">Tags</p>
                <div class="flex flex-wrap gap-2">
                  <button
                    v-for="tag in feedbackTagOptions"
                    :key="tag.value"
                    type="button"
                    @click="toggleTag(tag.value)"
                    :class="[
                      'rounded-full border px-2 py-1 text-xs transition',
                      feedbackTags.includes(tag.value)
                        ? 'border-sky-400 bg-sky-500/20 text-sky-100'
                        : 'border-white/15 bg-white/5 text-slate-300 hover:bg-white/10'
                    ]"
                  >
                    {{ tag.label }}
                  </button>
                </div>
              </div>

              <div class="mt-3">
                <label for="feedback-comment" class="mb-1 block text-xs uppercase tracking-wide text-slate-400">Comment</label>
                <textarea
                  id="feedback-comment"
                  v-model="feedbackComment"
                  rows="3"
                  class="w-full rounded-xl border border-white/10 bg-slate-900 px-3 py-2 text-sm text-white"
                />
              </div>

              <div v-if="feedbackMessage" class="mt-3 rounded-xl border border-emerald-400/20 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-100">
                {{ feedbackMessage }}
              </div>

              <div v-if="feedbackError" class="mt-3 rounded-xl border border-rose-400/20 bg-rose-500/10 px-3 py-2 text-sm text-rose-100">
                {{ feedbackError }}
              </div>

              <button
                type="button"
                :disabled="isSubmittingFeedback"
                @click="submitRatings"
                class="mt-3 w-full rounded-xl border border-white/10 bg-white/10 px-3 py-2 text-sm font-medium text-white transition hover:bg-white/15 disabled:cursor-not-allowed disabled:opacity-60"
              >
                {{ isSubmittingFeedback ? 'Submitting…' : 'Submit rankings' }}
              </button>
            </details>
          </div>
        </div>

        <div v-else class="mt-4 text-sm text-slate-400">Loading status…</div>
      </section>
    </div>

  </main>
</template>
