<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import type { PublicFeedItemResponse, FeedPageResponse } from '@/types/api'
import { useBackendRequestState } from '@/composables/useBackendRequestState'
import { useDevLoginAction } from '@/composables/useDevLoginAction'

const router = useRouter()
const authStore = useAuthStore()
const { loginDevAndRun } = useDevLoginAction()
const requestState = useBackendRequestState({
  retryDelayMs: 3000,
  offlineMessage: 'Waiting for backend to come online…'
})
const { isLoading, isLoadingMore, isWaitingForBackend, error, offlineMessage, retryCount } = requestState

const PAGE_SIZE = 12

const feed = ref<PublicFeedItemResponse[]>([])
const hasMore = ref(true)

// Cursor = publishedAtUtc of last loaded item
let cursor: string | null = null

function isFeedPageResponse(value: unknown): value is FeedPageResponse {
  if (!value || typeof value !== 'object') return false
  const maybeFeed = value as Partial<FeedPageResponse>
  return Array.isArray(maybeFeed.items) && typeof maybeFeed.hasMore === 'boolean'
}

function normalizeFeedPageResponse(value: unknown): FeedPageResponse {
  if (isFeedPageResponse(value)) return value

  // Backward compatibility with older backend shape: PublicFeedItemResponse[]
  if (Array.isArray(value)) {
    return {
      items: value as PublicFeedItemResponse[],
      hasMore: false
    }
  }

  return {
    items: [],
    hasMore: false
  }
}

async function fetchPage(loadMore = false, isRetry = false) {
  if (loadMore) {
    requestState.beginLoadMore()
  } else {
    if (!isRetry) {
      requestState.beginInitialLoad()
      feed.value = []
      cursor = null
      hasMore.value = true
    }
  }

  try {
    let url = `/style/feed?take=${PAGE_SIZE}`
    if (cursor) url += `&before=${encodeURIComponent(cursor)}`

    const response = await api.get<unknown>(url)
    const page = normalizeFeedPageResponse(response)

    feed.value = loadMore ? [...feed.value, ...page.items] : page.items
    hasMore.value = page.hasMore
    requestState.reset()

    if (page.items.length > 0) {
      cursor = page.items[page.items.length - 1].publishedAtUtc
    }
  } catch (err: unknown) {
    if (!loadMore && feed.value.length === 0) {
      requestState.handleError(err, () => fetchPage(false, true))
    } else {
      requestState.handleError(err, undefined, true)
    }
  } finally {
    requestState.finishLoad()
  }
}

// Infinite scroll sentinel
const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | null = null

onMounted(async () => {
  await fetchPage()

  observer = new IntersectionObserver(
    async (entries) => {
      if (entries[0].isIntersecting && !isLoadingMore.value && !isLoading.value && hasMore.value) {
        await fetchPage(true)
      }
    },
    { rootMargin: '200px' }
  )

  if (sentinel.value) observer.observe(sentinel.value)
})

onUnmounted(() => {
  observer?.disconnect()
})

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

async function handleSignIn() {
  await loginDevAndRun()
}

function openGenerate() {
  router.push({ name: 'style-generate' })
}

function openJobs() {
  router.push({ name: 'jobs' })
}
</script>

<template>
  <main class="mx-auto max-w-5xl px-4 pt-6 pb-8 sm:pt-10">
    <!-- Hero banner -->
    <section class="mb-6 overflow-hidden rounded-[2rem] border border-white/10 bg-white/5 p-6 shadow-2xl shadow-black/10 backdrop-blur sm:p-8">
      <div class="grid gap-6 lg:grid-cols-[1.2fr_0.8fr] lg:items-end">
        <div class="space-y-3">
          <p class="text-xs uppercase tracking-[0.3em] text-sky-200/70">Public feed</p>
          <h1 class="text-4xl font-semibold tracking-tight text-white sm:text-5xl">
            Discover public hair transformations.
          </h1>
          <p class="max-w-2xl text-sm leading-6 text-slate-300 sm:text-base">
            Browse results shared by other users, then jump into Generate to create your own look.
          </p>
        </div>

        <div class="flex flex-col gap-3 sm:flex-row lg:flex-col lg:justify-end">
          <button
            v-if="!authStore.isAuthenticated"
            type="button"
            @click="handleSignIn"
            class="rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-white transition hover:bg-sky-400"
          >
            Sign in
          </button>
          <button
            type="button"
            @click="openGenerate"
            class="rounded-2xl bg-white px-4 py-3 text-sm font-semibold text-slate-950 transition hover:bg-slate-100"
          >
            Generate a look
          </button>
          <button
            type="button"
            @click="openJobs"
            class="rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm font-semibold text-white transition hover:bg-white/10"
          >
            Your jobs
          </button>
        </div>
      </div>
    </section>

    <!-- Initial loading -->
    <div v-if="isLoading || isWaitingForBackend" class="space-y-3">
      <div class="grid grid-cols-2 gap-1 sm:grid-cols-3">
        <div
          v-for="n in PAGE_SIZE"
          :key="n"
          class="aspect-square animate-pulse rounded-2xl bg-white/5"
        />
      </div>
      <p v-if="isWaitingForBackend" class="text-center text-sm text-slate-400">
        {{ offlineMessage }} retrying every 3s
        <span v-if="retryCount > 0">(attempt {{ retryCount + 1 }})</span>
      </p>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="rounded-3xl border border-rose-400/20 bg-rose-500/10 p-4 text-rose-100">
      {{ error }}
    </div>

    <!-- Empty -->
    <div v-else-if="feed.length === 0" class="rounded-3xl border border-white/10 bg-white/5 p-8 text-center text-slate-200 shadow-xl shadow-black/10">
      <p class="text-lg font-medium">No public looks yet.</p>
      <p class="mt-2 text-sm text-slate-400">When users mark results public, they will appear here.</p>
    </div>

    <!-- Instagram-style grid -->
    <section v-else>
      <div class="grid grid-cols-2 gap-1 sm:grid-cols-3">
        <article
          v-for="item in feed"
          :key="item.jobId"
          class="group relative aspect-square cursor-pointer overflow-hidden rounded-2xl bg-slate-800"
        >
          <img
            :src="item.resultImageUrl"
            :alt="item.name"
            loading="lazy"
            class="h-full w-full object-cover transition duration-300 group-hover:scale-105"
          />
          <!-- gradient overlay always visible at bottom -->
          <div class="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/80 via-black/40 to-transparent pt-12 pb-3 px-3 transition-opacity duration-300">
            <p class="truncate text-sm font-semibold leading-tight text-white">{{ item.name }}</p>
            <p class="mt-0.5 text-[11px] text-slate-300">{{ formatDate(item.publishedAtUtc) }}</p>
          </div>
          <!-- hover overlay with description -->
          <div class="absolute inset-0 flex flex-col justify-end bg-black/60 p-3 opacity-0 transition-opacity duration-300 group-hover:opacity-100">
            <p class="text-sm font-semibold text-white">{{ item.name }}</p>
            <p class="mt-1 line-clamp-3 text-xs leading-relaxed text-slate-200">{{ item.description }}</p>
            <p class="mt-2 text-[11px] text-slate-400">{{ formatDate(item.publishedAtUtc) }}</p>
          </div>
        </article>
      </div>

      <!-- Infinite scroll sentinel -->
      <div ref="sentinel" class="mt-6 flex justify-center pb-2">
        <span v-if="isLoadingMore" class="animate-pulse text-sm text-slate-400">Loading more…</span>
        <span v-else-if="!hasMore && feed.length > 0" class="text-sm text-slate-500">All caught up</span>
      </div>
    </section>
  </main>
</template>

