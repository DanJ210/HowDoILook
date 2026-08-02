import { describe, expect, it } from 'vitest'
import {
  computeWorkflowStateLabel,
  hasGenerationFailed,
  isRecommendationSessionTerminal
} from './recommendationWorkflow'
import type { RecommendationRuntimeState } from './recommendationWorkflow'

describe('recommendation workflow utility', () => {
  it('returns analyzing while analysis is queued or processing', () => {
    expect(
      computeWorkflowStateLabel({
        hasActiveJob: true,
        analysisStatus: 'Queued',
        bestVariant: null,
        experimentalVariants: []
      })
    ).toBe('analyzing')

    expect(
      computeWorkflowStateLabel({
        hasActiveJob: true,
        analysisStatus: 'Processing',
        bestVariant: null,
        experimentalVariants: []
      })
    ).toBe('analyzing')
  })

  it('returns generating after analysis succeeds while the primary is pending', () => {
    const withoutPrimary = {
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      bestVariant: null,
      experimentalVariants: []
    } satisfies RecommendationRuntimeState
    const processingPrimary = {
      ...withoutPrimary,
      bestVariant: {
        generationJobId: 'best',
        status: 'Processing' as const,
        resultImageUrl: null
      }
    }

    expect(computeWorkflowStateLabel(withoutPrimary)).toBe('generating')
    expect(computeWorkflowStateLabel(processingPrimary)).toBe('generating')
    expect(isRecommendationSessionTerminal(processingPrimary)).toBe(false)
  })

  it('completes as soon as the automatic primary succeeds', () => {
    const state: RecommendationRuntimeState = {
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      bestVariant: {
        generationJobId: 'best',
        status: 'Succeeded',
        resultImageUrl: 'https://example.com/best.webp'
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'exp-1',
          status: 'Processing',
          resultImageUrl: null,
          selectedRank: null
        }
      ]
    }

    expect(computeWorkflowStateLabel(state)).toBe('completed')
    expect(hasGenerationFailed(state)).toBe(false)
    expect(isRecommendationSessionTerminal(state)).toBe(false)

    state.experimentalVariants[0].status = 'Succeeded'

    expect(computeWorkflowStateLabel(state)).toBe('completed')
    expect(isRecommendationSessionTerminal(state)).toBe(true)
  })

  it('waits for experimental jobs before reporting a failed primary', () => {
    const state: RecommendationRuntimeState = {
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      bestVariant: {
        generationJobId: 'best',
        status: 'Failed',
        resultImageUrl: null
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'exp-1',
          status: 'Processing',
          resultImageUrl: null,
          selectedRank: null
        }
      ]
    }

    expect(hasGenerationFailed(state)).toBe(false)
    expect(computeWorkflowStateLabel(state)).toBe('generating')
    expect(isRecommendationSessionTerminal(state)).toBe(false)
  })

  it('does not promote a succeeded experiment when the primary fails', () => {
    const state: RecommendationRuntimeState = {
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      bestVariant: {
        generationJobId: 'best',
        status: 'Failed',
        resultImageUrl: null
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'exp-1',
          status: 'Succeeded',
          resultImageUrl: 'https://example.com/experiment.webp',
          selectedRank: null
        }
      ]
    }

    expect(hasGenerationFailed(state)).toBe(true)
    expect(computeWorkflowStateLabel(state)).toBe('failed')
    expect(isRecommendationSessionTerminal(state)).toBe(true)
  })
})
