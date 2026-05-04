<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useStyleStore } from '@/stores/style'

const router = useRouter()
const styleStore = useStyleStore()

const form = ref({ name: '', description: '', prompt: '' })
const selectedFile = ref<File | null>(null)
const previewUrl = ref<string | null>(null)
const isUploading = ref(false)
const isSubmitting = ref(false)
const submitError = ref<string | null>(null)

const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif']
const MAX_SIZE_MB = 10

function onFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  setFile(input.files?.[0] ?? null)
}

function onDrop(event: DragEvent) {
  event.preventDefault()
  setFile(event.dataTransfer?.files?.[0] ?? null)
}

function setFile(file: File | null) {
  submitError.value = null

  if (!file) return

  if (!ACCEPTED_TYPES.includes(file.type)) {
    submitError.value = 'Only JPEG, PNG, WebP, or GIF images are allowed.'
    return
  }

  if (file.size > MAX_SIZE_MB * 1024 * 1024) {
    submitError.value = `Image must be smaller than ${MAX_SIZE_MB} MB.`
    return
  }

  selectedFile.value = file

  // Revoke any previous object URL to avoid memory leaks
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value)
  previewUrl.value = URL.createObjectURL(file)
}

function removeFile() {
  selectedFile.value = null
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = null
  }
}

async function handleSubmit() {
  isSubmitting.value = true
  isUploading.value = !!selectedFile.value
  submitError.value = null
  try {
    const resp = await styleStore.generate(form.value, selectedFile.value ?? undefined)
    router.push({ name: 'job-status', params: { id: resp.jobId } })
  } catch (err: unknown) {
    submitError.value = (err as { message?: string })?.message ?? 'Failed to submit request.'
  } finally {
    isSubmitting.value = false
    isUploading.value = false
  }
}

const buttonLabel = computed(() => {
  if (isUploading.value) return 'Uploading photo…'
  if (isSubmitting.value) return 'Submitting…'
  return 'Generate Style'
})

import { computed } from 'vue'
</script>

<template>
  <main class="max-w-xl mx-auto px-4 py-8">
    <button
      type="button"
      @click="router.back()"
      class="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-6 transition"
    >
      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
      </svg>
      Back
    </button>

    <h1 class="text-2xl font-bold mb-6">Generate Style</h1>

    <form @submit.prevent="handleSubmit" class="space-y-5">

      <!-- Photo upload -->
      <div>
        <label class="block text-sm font-medium mb-1">Your Photo <span class="text-gray-400 font-normal">(optional)</span></label>

        <div v-if="!previewUrl"
          class="border-2 border-dashed border-gray-300 rounded-lg p-6 text-center cursor-pointer hover:border-blue-400 transition"
          @click="($refs.fileInput as HTMLInputElement).click()"
          @dragover.prevent
          @drop="onDrop"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-10 w-10 text-gray-400 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
              d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
          </svg>
          <p class="text-sm text-gray-500">Drag &amp; drop or <span class="text-blue-600 underline">browse</span></p>
          <p class="text-xs text-gray-400 mt-1">JPEG, PNG, WebP, GIF — max 10 MB</p>
          <input ref="fileInput" type="file" accept="image/jpeg,image/png,image/webp,image/gif"
            class="hidden" @change="onFileChange" />
        </div>

        <div v-else class="relative rounded-lg overflow-hidden border border-gray-200">
          <img :src="previewUrl" alt="Selected photo" class="w-full max-h-64 object-cover" />
          <button
            type="button"
            @click="removeFile"
            class="absolute top-2 right-2 bg-black/60 hover:bg-black/80 text-white rounded-full w-7 h-7 flex items-center justify-center text-sm transition"
            title="Remove photo"
          >✕</button>
        </div>
      </div>

      <!-- Name -->
      <div>
        <label class="block text-sm font-medium mb-1" for="name">Name</label>
        <input
          id="name"
          v-model="form.name"
          required
          class="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="e.g. Urban Explorer"
        />
      </div>

      <!-- Description -->
      <div>
        <label class="block text-sm font-medium mb-1" for="description">Description</label>
        <textarea
          id="description"
          v-model="form.description"
          rows="3"
          required
          class="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="A short description of the style..."
        />
      </div>

      <!-- Prompt -->
      <div>
        <label class="block text-sm font-medium mb-1" for="prompt">Prompt</label>
        <textarea
          id="prompt"
          v-model="form.prompt"
          rows="4"
          required
          class="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="Detailed image generation prompt..."
        />
      </div>

      <div v-if="submitError" class="text-red-600 text-sm">{{ submitError }}</div>

      <button
        type="submit"
        :disabled="isSubmitting"
        class="w-full bg-blue-600 text-white py-2 px-4 rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition"
      >
        {{ buttonLabel }}
      </button>
    </form>
  </main>
</template>
