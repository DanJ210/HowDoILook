import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api/client'
import type { StyleItemResponse, GenerateStyleRequest, GenerateStyleResponse, UploadImageResponse } from '@/types/api'
import { useJobStore } from './job'

export const useStyleStore = defineStore('style', () => {
  const items = ref<StyleItemResponse[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  async function fetchAll() {
    isLoading.value = true
    error.value = null
    try {
      items.value = await api.get<StyleItemResponse[]>('/style')
    } catch (err: unknown) {
      error.value = (err as { message?: string })?.message ?? 'Failed to load styles.'
    } finally {
      isLoading.value = false
    }
  }

  async function generate(request: GenerateStyleRequest, imageFile?: File, isResultPublic = false): Promise<GenerateStyleResponse> {
    let imageUrl: string | undefined

    if (imageFile) {
      const uploaded = await api.upload<UploadImageResponse>('/upload/image', imageFile)
      imageUrl = uploaded.url
    }

    const resp = await api.post<GenerateStyleResponse>('/style/generate', { ...request, imageUrl, isResultPublic })

    // Start polling for the job
    const jobStore = useJobStore()
    jobStore.startPolling(resp.jobId, () => fetchAll())

    return resp
  }

  async function remove(id: string): Promise<void> {
    await api.delete(`/style/${id}`)
    items.value = items.value.filter(i => i.id !== id)
  }

  return { items, isLoading, error, fetchAll, generate, remove }
})
