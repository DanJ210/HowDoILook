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

