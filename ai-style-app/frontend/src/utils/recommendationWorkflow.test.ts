import { describe, expect, it } from 'vitest'
import { computeWorkflowStateLabel, hasGenerationFailed } from './recommendationWorkflow'
import type { RecommendationRuntimeState } from './recommendationWorkflow'

describe('recommendation workflow utility', () => {
  it('returns generating while analysis is queued or processing', () => {
    expect(
      computeWorkflowStateLabel({
        hasActiveJob: true,
        analysisStatus: 'Queued',
        selectedGenerationJobId: null,
        bestVariant: null,
        experimentalVariants: []
      })
    ).toBe('generating')

    expect(
      computeWorkflowStateLabel({
        hasActiveJob: true,
        analysisStatus: 'Processing',
        selectedGenerationJobId: null,
        bestVariant: null,
        experimentalVariants: []
      })
    ).toBe('generating')
  })

  it('returns ready_for_selection when at least one variant succeeded', () => {
    const label = computeWorkflowStateLabel({
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      selectedGenerationJobId: null,
      bestVariant: {
        generationJobId: 'best',
        status: 'Succeeded',
        resultImageUrl: 'https://example.com/best.webp'
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'exp-1',
          status: 'Failed',
          resultImageUrl: null,
          selectedRank: null
        }
      ]
    })

    expect(label).toBe('ready_for_selection')
    expect(
      hasGenerationFailed({
        hasActiveJob: true,
        analysisStatus: 'Succeeded',
        selectedGenerationJobId: null,
        bestVariant: {
          generationJobId: 'best',
          status: 'Succeeded',
          resultImageUrl: 'https://example.com/best.webp'
        },
        experimentalVariants: []
      })
    ).toBe(false)
  })

  it('returns failed when all variants completed without success', () => {
    const state: RecommendationRuntimeState = {
      hasActiveJob: true,
      analysisStatus: 'Succeeded',
      selectedGenerationJobId: null,
      bestVariant: {
        generationJobId: 'best',
        status: 'Failed',
        resultImageUrl: null
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'exp-1',
          status: 'TimedOut',
          resultImageUrl: null,
          selectedRank: null
        },
        {
          slot: 2,
          generationJobId: 'exp-2',
          status: 'Canceled',
          resultImageUrl: null,
          selectedRank: null
        }
      ]
    }

    expect(hasGenerationFailed(state)).toBe(true)
    expect(computeWorkflowStateLabel(state)).toBe('failed')
  })

  it('returns completed once final selection is set', () => {
    expect(
      computeWorkflowStateLabel({
        hasActiveJob: true,
        analysisStatus: 'Succeeded',
        selectedGenerationJobId: 'best',
        bestVariant: {
          generationJobId: 'best',
          status: 'Succeeded',
          resultImageUrl: 'https://example.com/best.webp'
        },
        experimentalVariants: []
      })
    ).toBe('completed')
  })
})