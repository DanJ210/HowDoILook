import type { RecommendationExperimentalVariant, RecommendationVariant } from '@/types/api'

export type RecommendationRuntimeState = {
  hasActiveJob: boolean
  analysisStatus: string | null
  bestVariant: RecommendationVariant | null
  experimentalVariants: readonly RecommendationExperimentalVariant[]
}

const terminalVariantStatuses = new Set(['Succeeded', 'Failed', 'TimedOut', 'Canceled'])
const failedAnalysisStatuses = new Set(['Failed', 'TimedOut', 'Canceled'])

export function hasGenerationFailed(state: RecommendationRuntimeState): boolean {
  if (!state.hasActiveJob || state.analysisStatus !== 'Succeeded' || !state.bestVariant) {
    return false
  }

  if (state.bestVariant.status === 'Succeeded' || !terminalVariantStatuses.has(state.bestVariant.status)) {
    return false
  }

  return state.experimentalVariants.every(variant => terminalVariantStatuses.has(variant.status))
}

export function computeWorkflowStateLabel(state: RecommendationRuntimeState): string {
  if (!state.hasActiveJob) {
    return 'analyzing'
  }

  if (state.analysisStatus && failedAnalysisStatuses.has(state.analysisStatus)) {
    return 'failed'
  }

  if (state.analysisStatus === 'Queued' || state.analysisStatus === 'Processing') {
    return 'analyzing'
  }

  if (state.analysisStatus === 'Succeeded' && state.bestVariant?.status === 'Succeeded') {
    return 'completed'
  }

  if (hasGenerationFailed(state)) {
    return 'failed'
  }

  if (state.analysisStatus === 'Succeeded') {
    return 'generating'
  }

  return 'analyzing'
}

export function isRecommendationSessionTerminal(state: RecommendationRuntimeState): boolean {
  if (!state.hasActiveJob) {
    return false
  }

  if (state.analysisStatus && failedAnalysisStatuses.has(state.analysisStatus)) {
    return true
  }

  if (state.analysisStatus !== 'Succeeded' || !state.bestVariant) {
    return false
  }

  return terminalVariantStatuses.has(state.bestVariant.status)
    && state.experimentalVariants.every(variant => terminalVariantStatuses.has(variant.status))
}