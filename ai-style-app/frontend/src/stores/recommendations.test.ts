import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useRecommendationsStore } from './recommendations'
import type { RecommendationJobStatusResponse } from '@/types/api'

function createRecommendationStatus(overrides: Partial<RecommendationJobStatusResponse> = {}): RecommendationJobStatusResponse {
  return {
    analysisJobId: 'job-1',
    recommendationPostId: null,
    publishStatus: null,
    status: 'Queued',
    qualityGate: {
      passed: null,
      failureCode: null,
      message: null
    },
    analysisSummary: {
      faceShape: null,
      confidence: null
    },
    bestRecommendation: null,
    bestVariant: null,
    experimentalVariants: [],
    recommendations: [],
    experiment: {
      enabled: true,
      trafficPercent: 100,
      applied: true,
      bucketKey: 'user-hash-00'
    },
    debugTelemetry: null,
    errorCode: null,
    errorMessage: null,
    primaryStyleId: null,
    primaryGenerationJobId: null,
    selectedGenerationJobId: null,
    selectedAtUtc: null,
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

  it('creates a recommendation from only an uploaded portrait', async () => {
    const store = useRecommendationsStore()
    const file = new File(['portrait'], 'portrait.jpg', { type: 'image/jpeg' })
    const created = {
      analysisJobId: 'job-1',
      recommendationPostId: 'post-1',
      status: 'Queued',
      statusEndpoint: '/api/recommendations/jobs/job-1',
      publicEndpoint: '/api/style/post-1'
    }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ url: 'https://example.com/portrait.jpg' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(created), { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(store.create({}, file)).resolves.toEqual(created)

    expect(fetchMock).toHaveBeenCalledTimes(2)
    const [, createOptions] = fetchMock.mock.calls[1]
    expect(JSON.parse(createOptions.body as string)).toEqual({
      imageUrl: 'https://example.com/portrait.jpg'
    })
  })

  it('continues polling after analysis succeeds until the primary generation succeeds', async () => {
    const store = useRecommendationsStore()

    const queued = createRecommendationStatus({ status: 'Queued' })
    const generating = createRecommendationStatus({
      status: 'Succeeded',
      bestVariant: {
        generationJobId: 'primary-1',
        status: 'Processing',
        resultImageUrl: null
      }
    })
    const completed = createRecommendationStatus({
      status: 'Succeeded',
      bestVariant: {
        generationJobId: 'primary-1',
        status: 'Succeeded',
        resultImageUrl: 'https://example.com/primary.webp'
      }
    })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(queued), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(generating), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(completed), { status: 200 }))

    vi.stubGlobal('fetch', fetchMock)

    store.startPolling('job-1')

    await vi.advanceTimersByTimeAsync(2000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('polling')

    await vi.advanceTimersByTimeAsync(3000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('polling')
    expect(store.getJob('job-1')?.bestVariant?.status).toBe('Processing')
    expect(store.activePollingIds.has('job-1')).toBe(true)

    await vi.advanceTimersByTimeAsync(4500)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('completed')
    expect(store.getJob('job-1')?.status).toBe('Succeeded')
    expect(store.getJob('job-1')?.bestVariant?.status).toBe('Succeeded')
    expect(store.activePollingIds.has('job-1')).toBe(false)
  })

  it('continues polling until delayed all-variants-failed status is available', async () => {
    const store = useRecommendationsStore()
    const experimentsProcessing = createRecommendationStatus({
      status: 'Succeeded',
      bestVariant: {
        generationJobId: 'primary-1',
        status: 'Failed',
        resultImageUrl: null
      },
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'experiment-1',
          status: 'Processing',
          resultImageUrl: null,
          selectedRank: null
        }
      ]
    })
    const allFailed = createRecommendationStatus({
      ...experimentsProcessing,
      experimentalVariants: [
        {
          slot: 1,
          generationJobId: 'experiment-1',
          status: 'TimedOut',
          resultImageUrl: null,
          selectedRank: null
        }
      ],
      errorCode: 'GENERATION_ALL_VARIANTS_FAILED',
      errorMessage: 'Generation completed without any successful variants.'
    })
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(experimentsProcessing), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(allFailed), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    store.startPolling('job-1')

    await vi.advanceTimersByTimeAsync(2000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('polling')
    expect(store.getJob('job-1')?.errorCode).toBeNull()

    await vi.advanceTimersByTimeAsync(3000)
    await Promise.resolve()

    expect(store.getPollingState('job-1')).toBe('completed')
    expect(store.getJob('job-1')?.errorCode).toBe('GENERATION_ALL_VARIANTS_FAILED')
    expect(store.activePollingIds.has('job-1')).toBe(false)
  })

  it('does not restart polling for a restored terminal session', () => {
    const store = useRecommendationsStore()
    store.jobs['job-1'] = createRecommendationStatus({
      status: 'Succeeded',
      bestVariant: {
        generationJobId: 'primary-1',
        status: 'Succeeded',
        resultImageUrl: 'https://example.com/primary.webp'
      }
    })
    const onComplete = vi.fn()

    store.startPolling('job-1', onComplete)

    expect(store.getPollingState('job-1')).toBe('completed')
    expect(store.activePollingIds.has('job-1')).toBe(false)
    expect(onComplete).toHaveBeenCalledWith(store.jobs['job-1'])
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

  it('submits recommendation ratings successfully', async () => {
    const store = useRecommendationsStore()

    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      store.submitRatings('job-1', {
        analysisJobId: 'job-1',
        rankings: [
          { generationJobId: 'gen-1', rank: 1 },
          { generationJobId: 'gen-2', rank: 2 },
          { generationJobId: 'gen-3', rank: 3 }
        ],
        feedbackTags: ['greatFit'],
        comment: 'Good spread'
      })
    ).resolves.toBeUndefined()
  })

  it('surfaces ratings submission errors', async () => {
    const store = useRecommendationsStore()

    const fetchMock = vi.fn().mockResolvedValue(new Response('ratings failed', { status: 500 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(
      store.submitRatings('job-1', {
        analysisJobId: 'job-1',
        rankings: [{ generationJobId: 'gen-1', rank: 1 }],
        feedbackTags: null,
        comment: null
      })
    ).rejects.toMatchObject({ message: 'ratings failed', statusCode: 500 })
  })
})
