<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()
const isWorking = ref(false)

const displayName = computed(() => authStore.user?.name?.trim() || 'User')
const initials = computed(() => {
  const name = displayName.value
  const first = name.charAt(0).toUpperCase()
  return first || 'U'
})

async function handleLogout() {
  isWorking.value = true
  try {
    await authStore.logout()
  } finally {
    isWorking.value = false
  }
}

async function handleLogin() {
  isWorking.value = true
  try {
    await authStore.login()
  } finally {
    isWorking.value = false
  }
}
</script>

<template>
  <div v-if="authStore.isReady" class="pointer-events-none fixed top-3 right-3 z-50 sm:top-4 sm:right-4">
    <div
      v-if="authStore.isAuthenticated"
      class="pointer-events-auto flex items-center gap-2 rounded-2xl border border-white/10 bg-slate-950/85 px-2 py-2 shadow-xl shadow-black/35 backdrop-blur-xl"
    >
      <div class="flex items-center gap-2 rounded-xl bg-white/5 px-2 py-1">
        <img
          v-if="authStore.user?.picture"
          :src="authStore.user.picture"
          :alt="displayName"
          class="h-7 w-7 rounded-full object-cover"
        />
        <span
          v-else
          class="flex h-7 w-7 items-center justify-center rounded-full bg-sky-500/80 text-[11px] font-semibold text-white"
        >
          {{ initials }}
        </span>
        <span class="max-w-[9rem] truncate text-xs font-medium text-slate-200">{{ displayName }}</span>
      </div>

      <button
        type="button"
        @click="handleLogout"
        :disabled="isWorking"
        class="rounded-xl border border-white/10 bg-white/5 px-3 py-1.5 text-xs font-semibold text-slate-100 transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-60"
      >
        Sign out
      </button>
    </div>

    <button
      v-else
      type="button"
      @click="handleLogin"
      :disabled="isWorking"
      class="pointer-events-auto rounded-2xl border border-white/10 bg-slate-950/85 px-4 py-2 text-xs font-semibold text-slate-100 shadow-xl shadow-black/35 backdrop-blur-xl transition hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-60"
    >
      Sign in
    </button>
  </div>
</template>
