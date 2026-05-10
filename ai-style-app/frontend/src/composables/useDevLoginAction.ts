import { useAuthStore } from '@/stores/auth'

export function useDevLoginAction() {
  const authStore = useAuthStore()

  async function loginDevAndRun(afterLogin?: () => void | Promise<void>) {
    await authStore.login()

    if (afterLogin) {
      await afterLogin()
    }
  }

  return {
    loginDevAndRun
  }
}
