<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useDevLoginAction } from '@/composables/useDevLoginAction'

const router = useRouter()
const authStore = useAuthStore()
const { loginDevAndRun } = useDevLoginAction()

const expiresAtLabel = computed(() => {
  if (!authStore.expiresAt) return 'No active session'

  return authStore.expiresAt.toLocaleString()
})

async function handleDevLogin() {
  await loginDevAndRun()
}

function handleLogout() {
  authStore.logout()
}

function goGenerate() {
  router.push({ name: 'style-generate' })
}

function goJobs() {
  router.push({ name: 'jobs' })
}
</script>

<template>
  <main class="mx-auto max-w-3xl px-4 pt-6 pb-8 sm:pt-10">
    <section class="overflow-hidden rounded-[2rem] border border-white/10 bg-white/5 p-6 shadow-2xl shadow-black/10 backdrop-blur sm:p-8">
      <p class="text-xs uppercase tracking-[0.3em] text-sky-200/70">Account</p>
      <h1 class="mt-2 text-3xl font-semibold tracking-tight text-white sm:text-4xl">Session controls</h1>

      <div v-if="!authStore.isAuthenticated" class="mt-6 space-y-4">
        <p class="text-sm leading-6 text-slate-300">
          You are currently signed out. Use Dev Login to access generation and job history routes.
        </p>
        <button
          type="button"
          class="rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-white transition hover:bg-sky-400"
          @click="handleDevLogin"
        >
          Dev Login
        </button>
      </div>

      <div v-else class="mt-6 space-y-4">
        <div class="rounded-2xl border border-white/10 bg-slate-900/60 p-4">
          <p class="text-xs uppercase tracking-[0.2em] text-slate-400">Status</p>
          <p class="mt-2 text-sm text-slate-200">Signed in</p>
          <p class="mt-1 text-xs text-slate-400">Expires: {{ expiresAtLabel }}</p>
        </div>

        <div class="flex flex-wrap gap-3">
          <button
            type="button"
            class="rounded-2xl bg-white px-4 py-3 text-sm font-semibold text-slate-950 transition hover:bg-slate-100"
            @click="goGenerate"
          >
            Generate a look
          </button>
          <button
            type="button"
            class="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm font-semibold text-white transition hover:bg-white/10"
            @click="goJobs"
          >
            View jobs
          </button>
          <button
            type="button"
            class="rounded-2xl px-4 py-3 text-sm font-semibold text-slate-300 transition hover:bg-white/10 hover:text-white"
            @click="handleLogout"
          >
            Logout
          </button>
        </div>
      </div>
    </section>
  </main>
</template>
