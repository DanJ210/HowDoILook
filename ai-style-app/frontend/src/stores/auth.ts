import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { api } from '@/api/client'
import type { TokenResponse } from '@/types/api'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('access_token'))
  const expiresAt = ref<Date | null>(
    localStorage.getItem('token_expires_at')
      ? new Date(localStorage.getItem('token_expires_at')!)
      : null
  )

  const isAuthenticated = computed(
    () => !!accessToken.value && (!expiresAt.value || expiresAt.value > new Date())
  )

  async function loginDev(username: string) {
    const resp = await api.post<TokenResponse>('/auth/token', { username, expiresInMinutes: 480 })
    setToken(resp.accessToken, resp.expiresAtUtc)
  }

  function setToken(token: string, expiresAtUtc: string) {
    accessToken.value = token
    expiresAt.value = new Date(expiresAtUtc)
    localStorage.setItem('access_token', token)
    localStorage.setItem('token_expires_at', expiresAtUtc)
  }

  function logout() {
    accessToken.value = null
    expiresAt.value = null
    localStorage.removeItem('access_token')
    localStorage.removeItem('token_expires_at')
  }

  return { accessToken, expiresAt, isAuthenticated, loginDev, setToken, logout }
})
