import { useAuthStore } from '@/stores/auth'

export function useDevLoginAction() {
  const authStore = useAuthStore()

  async function loginDevAndRun(afterLogin?: () => void | Promise<void>, username = 'dev-user') {
    await authStore.loginDev(username)

    if (afterLogin) {
      await afterLogin()
    }
  }

  return {
    loginDevAndRun
  }
}
