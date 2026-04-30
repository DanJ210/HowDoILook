<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useStyleStore } from '@/stores/style'

const router = useRouter()
const styleStore = useStyleStore()

const form = ref({ name: '', description: '', prompt: '' })
const isSubmitting = ref(false)
const submitError = ref<string | null>(null)

async function handleSubmit() {
  isSubmitting.value = true
  submitError.value = null
  try {
    const resp = await styleStore.generate(form.value)
    router.push({ name: 'job-status', params: { id: resp.jobId } })
  } catch (err: unknown) {
    submitError.value = (err as { message?: string })?.message ?? 'Failed to submit request.'
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <main class="max-w-xl mx-auto px-4 py-8">
    <h1 class="text-2xl font-bold mb-6">Generate Style</h1>

    <form @submit.prevent="handleSubmit" class="space-y-4">
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
        {{ isSubmitting ? 'Submitting…' : 'Generate Style' }}
      </button>
    </form>
  </main>
</template>
