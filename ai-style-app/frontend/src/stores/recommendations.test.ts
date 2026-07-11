import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useRecommendationsStore } from './recommendations'
import type { RecommendationJobStatusResponse } from '@/types/api'

function createRecommendationStatus(overrides: Partial<RecommendationJobStatusResponse> = {}): RecommendationJobStatusResponse {
  return {
    analysisJobId: 'job-1',
    status: 'Queued',
    qualityGate: {
      passed: null,
      failureCode: null,
      message: null
    },
    analysisSummary: {
      faceShapeDistribution: null,
      confidence: null
    },
    recommendations: [],
    errorCode: null,
    errorMessage: null,
    ...overrides
  }
}

describe('recommendations store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn()
    })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('transitions polling state to completed when job reaches terminal status', async () => {
    const store = useRecommendationsStore()

    const queued = createRecommendationStatus({ status: 'Queued' })
    const succeeded = createRecommendationStatus({ status: 'Succeeded' })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(queued), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(succeeded), { status: 200 }))

    vi.stubGlobal('fetch', fetchMock)

    store.startPolling('job-1')

    await vi.advanceTimersByTimeAsync(2000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('polling')

    await vi.advanceTimersByTimeAsync(3000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('completed')
    expect(store.getJob('job-1')?.status).toBe('Succeeded')
    expect(store.activePollingIds.has('job-1')).toBe(false)
  })

  it('stores failed quality-gate status payload from API', async () => {
    const store = useRecommendationsStore()

    const failed = createRecommendationStatus({
      status: 'Failed',
      qualityGate: {
        passed: false,
        failureCode: 'ANALYSIS_QUALITY_TOO_BLURRY',
        message: 'Image appears too blurry.'
      },
      errorCode: 'ANALYSIS_QUALITY_TOO_BLURRY',
      errorMessage: 'Image appears too blurry.'
    })

    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(failed), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const job = await store.fetchStatus('job-1')

    expect(job.status).toBe('Failed')
    expect(job.qualityGate.passed).toBe(false)
    expect(job.qualityGate.failureCode).toBe('ANALYSIS_QUALITY_TOO_BLURRY')
    expect(store.getJob('job-1')?.errorCode).toBe('ANALYSIS_QUALITY_TOO_BLURRY')
  })

  it('submits recommendation feedback successfully', async () => {
    const store = useRecommendationsStore()

    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      store.submitFeedback({
        analysisJobId: 'job-1',
        selectedStyleId: 'textured-crop',
        rating: 4,
        feedbackTags: ['greatFit'],
        comment: 'Solid suggestion.'
      })
    ).resolves.toBeUndefined()
  })

  it('surfaces feedback submission errors', async () => {
    const store = useRecommendationsStore()

    const fetchMock = vi.fn().mockResolvedValue(new Response('feedback failed', { status: 500 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      store.submitFeedback({
        analysisJobId: 'job-1',
        selectedStyleId: null,
        rating: null,
        feedbackTags: null,
        comment: null
      })
    ).rejects.toMatchObject({ message: 'feedback failed', statusCode: 500 })
  })
})
