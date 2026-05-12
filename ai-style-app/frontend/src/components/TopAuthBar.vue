<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useDevLoginAction } from '@/composables/useDevLoginAction'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { loginDevAndRun } = useDevLoginAction()

const isOnAccount = computed(() => route.name === 'account')
const expiresAtLabel = computed(() => {
  if (!authStore.expiresAt) return 'No expiration'

  return authStore.expiresAt.toLocaleString()
})

async function handleLogin() {
  await loginDevAndRun()
}

function goHome() {
  router.push({ name: 'home' })
}

function goAccount() {
  if (isOnAccount.value) return
  router.push({ name: 'account' })
}

function handleLogout() {
  authStore.logout()

  if (isOnAccount.value) {
    router.push({ name: 'home' })
  }
}
</script>

<template>
  <header class="fixed inset-x-0 top-0 z-50 px-4 pt-[calc(0.75rem+env(safe-area-inset-top))] sm:px-6">
    <div class="mx-auto flex w-full max-w-5xl items-center justify-between rounded-2xl border border-white/10 bg-slate-950/85 px-3 py-2 shadow-2xl shadow-black/30 backdrop-blur-xl sm:px-4">
      <button
        type="button"
        class="rounded-xl px-3 py-2 text-sm font-semibold text-slate-200 transition hover:bg-white/10 hover:text-white"
        @click="goHome"
      >
        HowDoILook
      </button>

      <div class="flex items-center gap-2">
        <p
          v-if="authStore.isAuthenticated"
          class="hidden text-xs text-slate-400 sm:block"
          :title="expiresAtLabel"
        >
          Signed in
        </p>

        <button
          v-if="!authStore.isAuthenticated"
          type="button"
          class="rounded-xl bg-sky-500 px-4 py-2 text-sm font-semibold text-white transition hover:bg-sky-400"
          @click="handleLogin"
        >
          Dev Login
        </button>

        <template v-else>
          <button
            type="button"
            class="rounded-xl border border-white/15 bg-white/5 px-4 py-2 text-sm font-semibold text-white transition hover:bg-white/10"
            @click="goAccount"
          >
            Account
          </button>
          <button
            type="button"
            class="rounded-xl px-3 py-2 text-sm font-semibold text-slate-300 transition hover:bg-white/10 hover:text-white"
            @click="handleLogout"
          >
            Logout
          </button>
        </template>
      </div>
    </div>
  </header>
</template>
