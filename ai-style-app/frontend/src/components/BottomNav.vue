<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

const navItems = [
  { name: 'home', label: 'Home', requiresAuth: false },
  { name: 'style-generate', label: 'Generate', requiresAuth: true },
  { name: 'jobs', label: 'Jobs', requiresAuth: true }
] as const

const currentRouteName = computed(() => route.name)

function isActive(name: string) {
  return currentRouteName.value === name
}

function go(name: string, requiresAuth = false) {
  if (requiresAuth && !authStore.isAuthenticated) {
    router.push({ name: 'home' })
    return
  }

  router.push({ name })
}
</script>

<template>
  <nav class="fixed inset-x-0 bottom-0 z-50 px-4 pb-[calc(1rem+env(safe-area-inset-bottom))]">
    <div class="mx-auto max-w-xl rounded-3xl border border-white/10 bg-slate-950/90 backdrop-blur-xl shadow-[0_-20px_60px_rgba(2,6,23,0.45)] px-3 py-3">
      <div class="grid grid-cols-3 gap-2">
        <button
          v-for="item in navItems"
          :key="item.name"
          type="button"
          :disabled="item.requiresAuth && !authStore.isAuthenticated"
          @click="go(item.name, !!item.requiresAuth)"
          :class="[
            'flex flex-col items-center justify-center rounded-2xl px-2 py-2 text-xs font-medium transition',
            isActive(item.name)
              ? 'bg-sky-500 text-white shadow-lg shadow-sky-500/25'
              : item.requiresAuth && !authStore.isAuthenticated
                ? 'cursor-not-allowed bg-white/5 text-slate-500'
                : 'text-slate-300 hover:bg-white/10 hover:text-white'
          ]"
        >
          <span class="leading-none">{{ item.label }}</span>
          <span
            v-if="item.requiresAuth && !authStore.isAuthenticated"
            class="mt-1 text-[10px] uppercase tracking-[0.2em] text-slate-500"
          >
            lock
          </span>
        </button>
      </div>
    </div>
  </nav>
</template>
