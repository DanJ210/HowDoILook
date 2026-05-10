import { computed, type ComputedRef, type Ref } from 'vue'

function parseJsonValue(json: string | null) {
  if (!json) return null

  try {
    return JSON.parse(json)
  } catch {
    return json
  }
}

function parseResultImages(json: string | null): string[] {
  if (!json) return []

  try {
    const parsed = JSON.parse(json)
    if (Array.isArray(parsed) && parsed.every((value: unknown) => typeof value === 'string')) {
      return parsed as string[]
    }

    if (typeof parsed === 'string') {
      return [parsed]
    }
  } catch {
    // fall through to empty array
  }

  return []
}

export function useJobResultViewModel(resultJson: Ref<string | null> | ComputedRef<string | null>, resultImageUrl: Ref<string | null> | ComputedRef<string | null>) {
  const parsedResult = computed(() => parseJsonValue(resultJson.value))
  const resultImages = computed(() => {
    if (resultImageUrl.value) return []
    return parseResultImages(resultJson.value)
  })

  return {
    parsedResult,
    resultImages
  }
}
