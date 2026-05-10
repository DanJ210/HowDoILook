import { onUnmounted, ref } from 'vue'

export interface ImageFileInputOptions {
  acceptedTypes?: string[]
  maxSizeMb?: number
}

const DEFAULT_ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif']
const DEFAULT_MAX_SIZE_MB = 10

export function useImageFileInput(options: ImageFileInputOptions = {}) {
  const acceptedTypes = options.acceptedTypes ?? DEFAULT_ACCEPTED_TYPES
  const maxSizeMb = options.maxSizeMb ?? DEFAULT_MAX_SIZE_MB

  const selectedFile = ref<File | null>(null)
  const previewUrl = ref<string | null>(null)

  function clearPreviewUrl() {
    if (previewUrl.value) {
      URL.revokeObjectURL(previewUrl.value)
      previewUrl.value = null
    }
  }

  function setFile(file: File | null): string | null {
    if (!file) return null

    if (!acceptedTypes.includes(file.type)) {
      return 'Only JPEG, PNG, WebP, or GIF images are allowed.'
    }

    if (file.size > maxSizeMb * 1024 * 1024) {
      return `Image must be smaller than ${maxSizeMb} MB.`
    }

    selectedFile.value = file
    clearPreviewUrl()
    previewUrl.value = URL.createObjectURL(file)
    return null
  }

  function onFileChange(event: Event): string | null {
    const input = event.target as HTMLInputElement
    return setFile(input.files?.[0] ?? null)
  }

  function onDrop(event: DragEvent): string | null {
    event.preventDefault()
    return setFile(event.dataTransfer?.files?.[0] ?? null)
  }

  function removeFile() {
    selectedFile.value = null
    clearPreviewUrl()
  }

  onUnmounted(() => {
    clearPreviewUrl()
  })

  return {
    selectedFile,
    previewUrl,
    setFile,
    onFileChange,
    onDrop,
    removeFile,
    clearPreviewUrl
  }
}
