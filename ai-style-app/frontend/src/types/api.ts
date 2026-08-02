export interface ApiError {
  message: string
  statusCode: number
}

// ── Style items ──────────────────────────────────────────────────────────────

export interface StyleItemResponse {
  id: string
  name: string
  description: string
  imageUrl: string | null
  isResultPublic: boolean
  createdAt: string
  latestJobId: string | null
  latestJobStatus: JobStatus | null
}

export interface UploadImageResponse {
  url: string
}

export interface PublicFeedItemResponse {
  styleItemId: string
  jobId: string
  name: string
  description: string
  resultImageUrl: string
  publishedAtUtc: string
}

export interface FeedPageResponse {
  items: PublicFeedItemResponse[]
  hasMore: boolean
}

export interface UserJobSummaryResponse {
  jobId: string
  styleItemId: string
  styleName: string
  status: JobStatus
  resultImageUrl: string | null
  isResultPublic: boolean
  createdAtUtc: string
  completedAtUtc: string | null
}

export interface UpdateJobVisibilityRequest {
  isResultPublic: boolean
}

// ── Jobs ─────────────────────────────────────────────────────────────────────

export type JobStatus = 'Queued' | 'Processing' | 'Succeeded' | 'Failed' | 'TimedOut' | 'Canceled'

export const TERMINAL_STATUSES: JobStatus[] = ['Succeeded', 'Failed', 'TimedOut', 'Canceled']

export interface JobStatusResponse {
  id: string
  styleItemId: string
  status: JobStatus
  jobType: string
  errorCode: string | null
  errorMessage: string | null
  resultJson: string | null
  resultImageUrl: string | null
  externalPredictionId: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  attemptCount: number
}

// ── Recommendations ──────────────────────────────────────────────────────────

export interface RecommendationPreferences {
  maintenanceLevel?: 'low' | 'medium' | 'high'
  styleVibe?: 'professional' | 'casual' | 'trendy'
  allowHairColorChange?: boolean
  allowBeardSuggestions?: boolean
}

export interface CreateRecommendationsRequest {
  imageUrl: string
  gender?: 'none' | 'male' | 'female'
  preferences?: RecommendationPreferences
}

export interface CreateRecommendationsResponse {
  analysisJobId: string
  recommendationPostId: string | null
  status: JobStatus
  statusEndpoint: string
  publicEndpoint: string | null
}

export interface RecommendationQualityGate {
  passed: boolean | null
  failureCode: string | null
  message: string | null
}

export interface RecommendationAnalysisSummary {
  faceShape: string | null
  confidence: number | null
}

export interface RecommendationItem {
  styleId: string
  styleName: string
  score: number
  reasons: string[]
  constraints: string[]
}

export interface RecommendationJobStatusResponse {
  analysisJobId: string
  recommendationPostId: string | null
  publishStatus: string | null
  status: JobStatus
  qualityGate: RecommendationQualityGate
  analysisSummary: RecommendationAnalysisSummary
  bestRecommendation: RecommendationItem | null
  bestVariant: RecommendationVariant | null
  experimentalVariants: RecommendationExperimentalVariant[]
  recommendations: RecommendationItem[]
  experiment: RecommendationExperiment
  debugTelemetry: RecommendationDebugTelemetry | null
  errorCode: string | null
  errorMessage: string | null
  primaryStyleId: string | null
  primaryGenerationJobId: string | null
  selectedGenerationJobId: string | null
  selectedAtUtc: string | null
}

export interface RecommendationVariant {
  generationJobId: string
  status: JobStatus
  resultImageUrl: string | null
}

export interface RecommendationExperimentalVariant {
  slot: number
  generationJobId: string
  status: JobStatus
  resultImageUrl: string | null
  selectedRank: string | null
}

export interface RecommendationExperiment {
  enabled: boolean
  trafficPercent: number
  applied: boolean
  bucketKey: string
}

export interface RecommendationStageTelemetry {
  stage: string
  model: string
  modelVersion: string
  durationMs: number
  metrics: Record<string, number> | null
  notes: string | null
}

export interface RecommendationDebugTelemetry {
  source: string | null
  schemaVersion: number | null
  imageWidth: number | null
  imageHeight: number | null
  stages: RecommendationStageTelemetry[]
}

export interface RecommendationRankingInput {
  generationJobId: string
  rank: 1 | 2 | 3
}

export interface SubmitRecommendationRatingsRequest {
  analysisJobId?: string
  rankings: RecommendationRankingInput[]
  feedbackTags: string[] | null
  comment: string | null
}

export interface FinalizeRecommendationRequest {
  generationJobId: string
}

export interface FinalizeRecommendationResponse {
  analysisJobId: string
  selectedGenerationJobId: string
  selectedAtUtc: string
  alreadyFinalized: boolean
}

// ── Auth ─────────────────────────────────────────────────────────────────────

export interface DevTokenRequest {
  username: string
  expiresInMinutes?: number
}

export interface TokenResponse {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
}
