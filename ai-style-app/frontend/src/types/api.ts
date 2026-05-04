export interface ApiError {
  message: string
  statusCode: number
}

export interface ApiResponse<T> {
  data: T
  success: boolean
}

// ── Style items ──────────────────────────────────────────────────────────────

export interface StyleItemResponse {
  id: string
  name: string
  description: string
  imageUrl: string | null
  createdAt: string
  latestJobId: string | null
  latestJobStatus: JobStatus | null
}

export interface GenerateStyleRequest {
  name: string
  description: string
  prompt: string
  imageUrl?: string
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

