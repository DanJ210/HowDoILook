import type { RecommendationExperimentalVariant, RecommendationVariant } from '@/types/api'

export type RecommendationRuntimeState = {
  hasActiveJob: boolean
  analysisStatus: string | null
  selectedGenerationJobId: string | null
  bestVariant: RecommendationVariant | null
  experimentalVariants: readonly RecommendationExperimentalVariant[]
}

const terminalVariantStatuses = new Set(['Succeeded', 'Failed', 'TimedOut', 'Canceled'])

export function hasGenerationFailed(state: RecommendationRuntimeState): boolean {
  if (!state.hasActiveJob || state.analysisStatus !== 'Succeeded' || state.selectedGenerationJobId) {
    return false
  }

  const variants = collectVariants(state.bestVariant, state.experimentalVariants)
  if (variants.length === 0) {
    return false
  }

  const allTerminal = variants.every(variant => terminalVariantStatuses.has(variant.status))
  if (!allTerminal) {
    return false
  }

  const succeededCount = variants.filter(variant => variant.status === 'Succeeded').length
  return succeededCount === 0
}

export function computeWorkflowStateLabel(state: RecommendationRuntimeState): string {
  if (!state.hasActiveJob) {
    return 'analyzing'
  }

  if (state.selectedGenerationJobId) {
    return 'completed'
  }

  if (state.analysisStatus === 'Failed') {
    return 'failed'
  }

  if (state.analysisStatus === 'Queued' || state.analysisStatus === 'Processing') {
    return 'generating'
  }

  if (hasGenerationFailed(state)) {
    return 'failed'
  }

  if (state.analysisStatus === 'Succeeded') {
    return 'ready_for_selection'
  }

  return 'analyzing'
}

function collectVariants(
  bestVariant: RecommendationVariant | null,
  experimentalVariants: readonly RecommendationExperimentalVariant[]
): Array<{ generationJobId: string; status: string }> {
  const variants: Array<{ generationJobId: string; status: string }> = []
  const seen = new Set<string>()

  if (bestVariant && !seen.has(bestVariant.generationJobId)) {
    seen.add(bestVariant.generationJobId)
    variants.push({ generationJobId: bestVariant.generationJobId, status: bestVariant.status })
  }

  for (const variant of experimentalVariants) {
    if (seen.has(variant.generationJobId)) {
      continue
    }

    seen.add(variant.generationJobId)
    variants.push({ generationJobId: variant.generationJobId, status: variant.status })
  }

  return variants
}