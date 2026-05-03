<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useStyleStore } from '@/stores/style'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const styleStore = useStyleStore()
const authStore = useAuthStore()

onMounted(() => {
  if (authStore.isAuthenticated) {
    styleStore.fetchAll()
  }
})

async function handleDevLogin() {
  await authStore.loginDev('dev-user')
  await styleStore.fetchAll()
}

function viewJob(jobId: string) {
  router.push({ name: 'job-status', params: { id: jobId } })
}
</script>

<template>
  <main class="max-w-3xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-8">
      <h1 class="text-3xl font-bold">AI Style App</h1>
      <div v-if="!authStore.isAuthenticated">
        <button
          @click="handleDevLogin"
          class="bg-gray-800 text-white px-4 py-2 rounded hover:bg-gray-700 transition text-sm"
        >
          Dev Login
        </button>
      </div>
      <div v-else class="flex gap-3 items-center">
        <span class="text-sm text-gray-500">Signed in</span>
        <button
          @click="router.push({ name: 'style-generate' })"
          class="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700 transition text-sm"
        >
          + Generate Style
        </button>
        <button
          @click="authStore.logout()"
          class="text-sm text-gray-500 hover:text-gray-700 underline"
        >
          Logout
        </button>
      </div>
    </div>

    <div v-if="!authStore.isAuthenticated" class="text-center py-16 text-gray-400">
      <p class="text-lg">Sign in to manage your styles.</p>
    </div>

    <div v-else-if="styleStore.isLoading" class="text-center py-16 text-gray-400 animate-pulse">
      Loading…
    </div>

    <div v-else-if="styleStore.error" class="p-4 bg-red-50 border border-red-200 rounded text-red-700">
      {{ styleStore.error }}
    </div>

    <div v-else-if="styleStore.items.length === 0" class="text-center py-16 text-gray-400">
      <p class="text-lg mb-4">No styles yet.</p>
      <button
        @click="router.push({ name: 'style-generate' })"
        class="bg-blue-600 text-white px-6 py-2 rounded hover:bg-blue-700 transition"
      >
        Generate your first style
      </button>
    </div>

    <ul v-else class="space-y-4">
      <li
        v-for="item in styleStore.items"
        :key="item.id"
        class="border rounded-lg p-4 flex justify-between items-start hover:shadow-sm transition"
      >
        <div>
          <h2 class="font-semibold text-lg">{{ item.name }}</h2>
          <p class="text-gray-600 text-sm mt-1">{{ item.description }}</p>
          <p class="text-gray-400 text-xs mt-2">{{ new Date(item.createdAt).toLocaleString() }}</p>
        </div>
        <div class="ml-4 shrink-0 flex items-center gap-3">
          <button
            v-if="item.latestJobId"
            @click="viewJob(item.latestJobId)"
            class="bg-slate-100 text-slate-700 px-3 py-1.5 rounded hover:bg-slate-200 transition text-sm"
          >
            View Job
          </button>
          <button
            @click="styleStore.remove(item.id)"
            class="text-red-400 hover:text-red-600 text-sm"
            title="Delete"
          >
            Delete
          </button>
        </div>
      </li>
    </ul>
  </main>
</template>

