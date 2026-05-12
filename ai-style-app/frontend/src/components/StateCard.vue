<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  title?: string
  description?: string
  tone?: 'default' | 'error'
  centered?: boolean
  paddingClass?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  description: '',
  tone: 'default',
  centered: true,
  paddingClass: 'p-6'
})

const wrapperClass = computed(() => {
  if (props.tone === 'error') {
    return 'rounded-3xl border border-rose-400/20 bg-rose-500/10 text-rose-100'
  }

  return 'rounded-3xl border border-white/10 bg-white/5 text-slate-200 shadow-xl shadow-black/10'
})

const descriptionClass = computed(() => {
  if (props.tone === 'error') {
    return 'mt-2 text-sm text-rose-100/90'
  }

  return 'mt-2 text-sm text-slate-400'
})
</script>

<template>
  <div :class="[wrapperClass, centered ? 'text-center' : '', paddingClass]">
    <p v-if="title" class="text-lg font-medium">{{ title }}</p>
    <p v-if="description" :class="descriptionClass">{{ description }}</p>

    <div v-if="$slots.default" class="mt-4">
      <slot />
    </div>
  </div>
</template>
