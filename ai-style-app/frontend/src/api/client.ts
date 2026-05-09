import type { ApiError } from '@/types/api'

const BASE_URL = '/api'

function getAuthHeader(): Record<string, string> {
  const token = localStorage.getItem('access_token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(`${BASE_URL}${path}`, {
      headers: {
        'Content-Type': 'application/json',
        ...getAuthHeader(),
        ...options?.headers
      },
      ...options
    })
  } catch (err: unknown) {
    const error: ApiError = {
      message: (err as { message?: string })?.message ?? 'Unable to reach the backend.',
      statusCode: 0
    }
    throw error
  }

  if (!response.ok) {
    const error: ApiError = {
      message: await response.text(),
      statusCode: response.status
    }
    throw error
  }

  // 204 No Content — return undefined cast to T
  if (response.status === 204) return undefined as T

  return response.json() as Promise<T>
}

async function uploadFile<T>(path: string, file: File): Promise<T> {
  const form = new FormData()
  form.append('file', file)

  let response: Response

  try {
    response = await fetch(`${BASE_URL}${path}`, {
      method: 'POST',
      // Do NOT set Content-Type — the browser must set it with the multipart boundary
      headers: { ...getAuthHeader() },
      body: form
    })
  } catch (err: unknown) {
    const error: ApiError = {
      message: (err as { message?: string })?.message ?? 'Unable to reach the backend.',
      statusCode: 0
    }
    throw error
  }

  if (!response.ok) {
    const error: ApiError = {
      message: await response.text(),
      statusCode: response.status
    }
    throw error
  }

  return response.json() as Promise<T>
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body: unknown) =>
    request<T>(path, { method: 'POST', body: JSON.stringify(body) }),
  put: <T>(path: string, body: unknown) =>
    request<T>(path, { method: 'PUT', body: JSON.stringify(body) }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  upload: <T>(path: string, file: File) => uploadFile<T>(path, file)
}

