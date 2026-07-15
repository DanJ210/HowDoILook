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

export interface GenerateStyleRequest {
  name: string
  description: string
  prompt?: string
  imageUrl?: string
  isResultPublic?: boolean
  haircut?: string
  hairColor?: string
  beardStyle?: string
  beardColor?: string
  gender?: string
}

export interface UploadImageResponse {
  url: string
}

export interface GenerateStyleResponse {
  jobId: string
  styleItemId: string
  status: string
  statusEndpoint: string
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
  status: JobStatus
  statusEndpoint: string
}

export interface RecommendationQualityGate {
  passed: boolean | null
  failureCode: string | null
  message: string | null
}

export interface RecommendationAnalysisSummary {
  faceShapeDistribution: Record<string, number> | null
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
  status: JobStatus
  qualityGate: RecommendationQualityGate
  analysisSummary: RecommendationAnalysisSummary
  recommendations: RecommendationItem[]
  debugTelemetry: RecommendationDebugTelemetry | null
  errorCode: string | null
  errorMessage: string | null
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

export interface SubmitRecommendationFeedbackRequest {
  analysisJobId: string
  selectedStyleId: string | null
  rating: number | null
  feedbackTags: string[] | null
  comment: string | null
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
