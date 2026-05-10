import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { createAuth0Client, type Auth0Client, type User } from '@auth0/auth0-spa-js'

type AuthMode = 'dev' | 'auth0'

interface AppUser {
  id: string
  name: string
  email: string | null
  picture: string | null
}

const authMode: AuthMode =
  import.meta.env.VITE_AUTH_MODE?.toLowerCase() === 'auth0' ? 'auth0' : 'dev'

const auth0Domain = import.meta.env.VITE_AUTH0_DOMAIN ?? ''
const auth0ClientId = import.meta.env.VITE_AUTH0_CLIENT_ID ?? ''
const auth0Audience = import.meta.env.VITE_AUTH0_AUDIENCE ?? ''

let auth0Client: Auth0Client | null = null

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(authMode === 'dev' ? localStorage.getItem('access_token') : null)
  const expiresAt = ref<Date | null>(null)
  const user = ref<AppUser | null>(null)
  const isReady = ref(false)
  const authError = ref<string | null>(null)

  if (authMode === 'dev') {
    const expiresAtRaw = localStorage.getItem('token_expires_at')
    expiresAt.value = expiresAtRaw ? new Date(expiresAtRaw) : null
  }

  const isAuthenticated = computed(
    () => !!accessToken.value && (!expiresAt.value || expiresAt.value > new Date())
  )

  async function initialize() {
    authError.value = null

    if (authMode === 'auth0') {
      try {
        const client = await ensureAuth0Client()
        const authenticated = await client.isAuthenticated()

        if (authenticated) {
          await hydrateAuth0Session(client)
        } else {
          clearLocalSession()
        }
      } catch (err: unknown) {
        clearLocalSession()
        authError.value = (err as { message?: string })?.message ?? 'Unable to initialize authentication.'
      }
    }

    isReady.value = true
  }

  async function login() {
    authError.value = null

    if (authMode === 'auth0') {
      const client = await ensureAuth0Client()
      await client.loginWithPopup()
      await hydrateAuth0Session(client)
      return
    }

    await loginDev('dev-user')
  }

  async function loginDev(username: string) {
    const response = await fetch('/api/auth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, expiresInMinutes: 480 })
    })

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || 'Unable to authenticate in development mode.')
    }

    const resp = await response.json() as { accessToken: string; expiresAtUtc: string }
    setToken(resp.accessToken, resp.expiresAtUtc)
  }

  function setToken(token: string, expiresAtUtc: string) {
    accessToken.value = token
    expiresAt.value = new Date(expiresAtUtc)

    if (authMode === 'dev') {
      localStorage.setItem('access_token', token)
      localStorage.setItem('token_expires_at', expiresAtUtc)
    }
  }

  async function getAccessToken() {
    if (authMode === 'auth0') {
      if (!auth0Client) {
        return null
      }

      try {
        const token = await auth0Client.getTokenSilently()
        accessToken.value = token
        expiresAt.value = parseTokenExpiration(token)
        return token
      } catch {
        clearLocalSession()
        return null
      }
    }

    return accessToken.value
  }

  async function logout() {
    if (authMode === 'auth0' && auth0Client) {
      clearLocalSession()
      await auth0Client.logout({ logoutParams: { returnTo: window.location.origin } })
      return
    }

    clearLocalSession()
  }

  function clearLocalSession() {
    accessToken.value = null
    expiresAt.value = null
    user.value = null
    localStorage.removeItem('access_token')
    localStorage.removeItem('token_expires_at')
  }

  async function ensureAuth0Client(): Promise<Auth0Client> {
    if (auth0Client) {
      return auth0Client
    }

    if (!auth0Domain || !auth0ClientId) {
      throw new Error(
        'Missing Auth0 configuration. Set VITE_AUTH0_DOMAIN and VITE_AUTH0_CLIENT_ID in frontend env.'
      )
    }

    const client = await createAuth0Client({
      domain: auth0Domain,
      clientId: auth0ClientId,
      authorizationParams: {
        redirect_uri: window.location.origin,
        audience: auth0Audience || undefined
      },
      cacheLocation: 'memory',
      useRefreshTokens: true
    })

    auth0Client = client
    return client
  }

  async function hydrateAuth0Session(client: Auth0Client) {
    const token = await client.getTokenSilently()
    const auth0User = await client.getUser()

    accessToken.value = token
    expiresAt.value = parseTokenExpiration(token)
    user.value = mapUser(auth0User)
  }

  function parseTokenExpiration(token: string): Date | null {
    const parts = token.split('.')
    if (parts.length < 2) return null

    try {
      const payload = JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')))
      return typeof payload.exp === 'number' ? new Date(payload.exp * 1000) : null
    } catch {
      return null
    }
  }

  function mapUser(auth0User: User | undefined): AppUser | null {
    if (!auth0User?.sub) {
      return null
    }

    return {
      id: auth0User.sub,
      name: auth0User.name ?? auth0User.nickname ?? 'User',
      email: auth0User.email ?? null,
      picture: auth0User.picture ?? null
    }
  }

  return {
    authMode,
    accessToken,
    expiresAt,
    user,
    isAuthenticated,
    isReady,
    authError,
    initialize,
    login,
    loginDev,
    getAccessToken,
    setToken,
    logout
  }
})
