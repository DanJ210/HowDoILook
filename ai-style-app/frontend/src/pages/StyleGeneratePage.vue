<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useStyleStore } from '@/stores/style'
import { useBackendRequestState } from '@/composables/useBackendRequestState'
import { useDevLoginAction } from '@/composables/useDevLoginAction'
import { useImageFileInput } from '@/composables/useImageFileInput'

const router = useRouter()
const authStore = useAuthStore()
const styleStore = useStyleStore()
const { loginDevAndRun } = useDevLoginAction()
const requestState = useBackendRequestState()
const { selectedFile, previewUrl, onFileChange: onFileChangeFromInput, onDrop: onDropFromInput, removeFile } = useImageFileInput()

const form = ref({ name: '', description: '', haircut: 'No change', hairColor: 'No change', gender: 'none' })
const isResultPublic = ref(false)
const isUploading = ref(false)
const isSubmitting = ref(false)
const lastErrorWasNetwork = ref(false)

// Alias for convenience — composable owns the reactive ref
const submitError = requestState.error

function onFileChange(event: Event) {
  submitError.value = null
  lastErrorWasNetwork.value = false
  const fileError = onFileChangeFromInput(event)
  if (fileError) {
    submitError.value = fileError
  }
}

function onDrop(event: DragEvent) {
  submitError.value = null
  lastErrorWasNetwork.value = false
  const fileError = onDropFromInput(event)
  if (fileError) {
    submitError.value = fileError
  }
}

function handleRemoveFile() {
  removeFile()
  isResultPublic.value = false
  submitError.value = null
  lastErrorWasNetwork.value = false
}

async function handleSubmit() {
  if (!selectedFile.value) {
    submitError.value = 'Please upload a photo before generating a style.'
    lastErrorWasNetwork.value = false
    return
  }

  isSubmitting.value = true
  isUploading.value = true
  submitError.value = null
  lastErrorWasNetwork.value = false
  try {
    const resp = await styleStore.generate(form.value, selectedFile.value ?? undefined, isResultPublic.value)
    router.push({ name: 'job-status', params: { id: resp.jobId } })
  } catch (err: unknown) {
    // No auto-retry — submitting is a mutation; user retries explicitly
    lastErrorWasNetwork.value = requestState.isOfflineError(err)
    requestState.handleError(err)
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

async function handleDevLogin() {
  await loginDevAndRun()
}
</script>

<template>
  <main class="mx-auto max-w-2xl px-4 py-6 sm:py-8">
    <button
      type="button"
      @click="router.back()"
      class="mb-5 flex items-center gap-1 text-sm text-slate-300 transition hover:text-white sm:mb-6"
    >
      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
      </svg>
      Back
    </button>

    <h1 class="mb-5 text-2xl font-bold text-white sm:mb-6">Generate Style</h1>

    <div v-if="!authStore.isAuthenticated" class="rounded-3xl border border-white/10 bg-white/5 p-6 text-slate-200 shadow-xl shadow-black/10 backdrop-blur">
      <p class="text-lg font-medium">Sign in to generate a style.</p>
      <p class="mt-2 text-sm text-slate-400">Use the dev login for local development.</p>
      <button
        type="button"
        @click="handleDevLogin"
        class="mt-4 rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-white transition hover:bg-sky-400"
      >
        Dev Login
      </button>
    </div>

    <form v-else @submit.prevent="handleSubmit" class="space-y-6">

      <!-- Photo upload -->
      <div>
        <label class="block text-sm font-medium mb-1">Your Photo <span class="text-red-500">*</span></label>

        <div v-if="!previewUrl"
          class="border-2 border-dashed border-gray-300 rounded-lg p-4 sm:p-6 text-center cursor-pointer hover:border-blue-400 transition"
          @click="($refs.fileInput as HTMLInputElement).click()"
          @dragover.prevent
          @drop="onDrop"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-10 w-10 text-gray-400 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
              d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
          </svg>
          <p class="text-sm sm:text-base text-gray-500">Drag &amp; drop or <span class="text-blue-600 underline">browse</span></p>
          <p class="text-xs text-gray-400 mt-1">JPEG, PNG, WebP, GIF — max 10 MB</p>
          <input ref="fileInput" type="file" accept="image/jpeg,image/png,image/webp,image/gif"
            class="hidden" @change="onFileChange" />
        </div>

        <div v-else class="relative rounded-lg overflow-hidden border border-gray-200">
          <img :src="previewUrl" alt="Selected photo" class="w-full max-h-64 object-cover" />
          <button
            type="button"
            @click="handleRemoveFile"
            class="absolute top-2 right-2 bg-black/60 hover:bg-black/80 text-white rounded-full w-9 h-9 sm:w-7 sm:h-7 flex items-center justify-center text-sm transition"
            title="Remove photo"
          >✕</button>
        </div>

        <!-- Visibility toggle — only shown when an image is attached -->
        <div v-if="selectedFile" class="mt-3 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between rounded-lg border border-gray-200 px-4 py-3">
          <div class="min-w-0">
            <p class="text-sm font-medium">Make result public</p>
            <p class="text-xs text-gray-400 mt-0.5">Anyone with the link can view the generated image</p>
          </div>
          <button
            type="button"
            role="switch"
            :aria-checked="isResultPublic"
            @click="isResultPublic = !isResultPublic"
            :class="[
              'relative inline-flex h-7 w-12 sm:h-6 sm:w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 self-start sm:self-auto',
              isResultPublic ? 'bg-blue-600' : 'bg-gray-200'
            ]"
          >
            <span
              :class="[
                'pointer-events-none inline-block h-6 w-6 sm:h-5 sm:w-5 rounded-full bg-white shadow transform transition duration-200',
                isResultPublic ? 'translate-x-5 sm:translate-x-5' : 'translate-x-0'
              ]"
            />
          </button>
        </div>
      </div>

      <!-- Name -->
      <div>
        <label class="block text-sm font-medium mb-1" for="name">Name</label>
        <input
          id="name"
          v-model="form.name"
          required
          class="w-full border rounded px-3 py-3 sm:py-2 text-base sm:text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
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
          class="w-full border rounded px-3 py-3 sm:py-2 text-base sm:text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="A short description of the style..."
        />
      </div>

      <!-- Haircut -->
      <div>
        <label class="block text-sm font-medium mb-1" for="haircut">Haircut</label>
        <select
          id="haircut"
          v-model="form.haircut"
          class="w-full border rounded px-3 py-3 sm:py-2 text-base sm:text-sm bg-white text-slate-900 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="No change">No change</option>
          <option value="Random">Random</option>
          <option value="Straight">Straight</option>
          <option value="Wavy">Wavy</option>
          <option value="Curly">Curly</option>
          <option value="Bob">Bob</option>
          <option value="Pixie Cut">Pixie Cut</option>
          <option value="Layered">Layered</option>
          <option value="Messy Bun">Messy Bun</option>
          <option value="High Ponytail">High Ponytail</option>
          <option value="Low Ponytail">Low Ponytail</option>
          <option value="Braided Ponytail">Braided Ponytail</option>
          <option value="French Braid">French Braid</option>
          <option value="Dutch Braid">Dutch Braid</option>
          <option value="Fishtail Braid">Fishtail Braid</option>
          <option value="Space Buns">Space Buns</option>
          <option value="Top Knot">Top Knot</option>
          <option value="Undercut">Undercut</option>
          <option value="Mohawk">Mohawk</option>
          <option value="Crew Cut">Crew Cut</option>
          <option value="Faux Hawk">Faux Hawk</option>
          <option value="Slicked Back">Slicked Back</option>
          <option value="Side-Parted">Side-Parted</option>
          <option value="Center-Parted">Center-Parted</option>
          <option value="Blunt Bangs">Blunt Bangs</option>
          <option value="Side-Swept Bangs">Side-Swept Bangs</option>
          <option value="Shag">Shag</option>
          <option value="Lob">Lob</option>
          <option value="Angled Bob">Angled Bob</option>
          <option value="A-Line Bob">A-Line Bob</option>
          <option value="Asymmetrical Bob">Asymmetrical Bob</option>
          <option value="Graduated Bob">Graduated Bob</option>
          <option value="Inverted Bob">Inverted Bob</option>
          <option value="Layered Shag">Layered Shag</option>
          <option value="Choppy Layers">Choppy Layers</option>
          <option value="Razor Cut">Razor Cut</option>
          <option value="Perm">Perm</option>
          <option value="Ombré">Ombré</option>
          <option value="Straightened">Straightened</option>
          <option value="Soft Waves">Soft Waves</option>
          <option value="Glamorous Waves">Glamorous Waves</option>
          <option value="Hollywood Waves">Hollywood Waves</option>
          <option value="Finger Waves">Finger Waves</option>
          <option value="Tousled">Tousled</option>
          <option value="Feathered">Feathered</option>
          <option value="Pageboy">Pageboy</option>
          <option value="Pigtails">Pigtails</option>
          <option value="Pin Curls">Pin Curls</option>
          <option value="Rollerset">Rollerset</option>
          <option value="Twist Out">Twist Out</option>
          <option value="Bantu Knots">Bantu Knots</option>
          <option value="Dreadlocks">Dreadlocks</option>
          <option value="Cornrows">Cornrows</option>
          <option value="Box Braids">Box Braids</option>
          <option value="Crochet Braids">Crochet Braids</option>
          <option value="Double Dutch Braids">Double Dutch Braids</option>
          <option value="French Fishtail Braid">French Fishtail Braid</option>
          <option value="Waterfall Braid">Waterfall Braid</option>
          <option value="Rope Braid">Rope Braid</option>
          <option value="Heart Braid">Heart Braid</option>
          <option value="Halo Braid">Halo Braid</option>
          <option value="Crown Braid">Crown Braid</option>
          <option value="Braided Crown">Braided Crown</option>
          <option value="Bubble Braid">Bubble Braid</option>
          <option value="Bubble Ponytail">Bubble Ponytail</option>
          <option value="Ballerina Braids">Ballerina Braids</option>
          <option value="Milkmaid Braids">Milkmaid Braids</option>
          <option value="Bohemian Braids">Bohemian Braids</option>
          <option value="Flat Twist">Flat Twist</option>
          <option value="Crown Twist">Crown Twist</option>
          <option value="Twisted Bun">Twisted Bun</option>
          <option value="Twisted Half-Updo">Twisted Half-Updo</option>
          <option value="Twist and Pin Updo">Twist and Pin Updo</option>
          <option value="Chignon">Chignon</option>
          <option value="Simple Chignon">Simple Chignon</option>
          <option value="Messy Chignon">Messy Chignon</option>
          <option value="French Twist">French Twist</option>
          <option value="French Twist Updo">French Twist Updo</option>
          <option value="French Roll">French Roll</option>
          <option value="Updo">Updo</option>
          <option value="Messy Updo">Messy Updo</option>
          <option value="Knotted Updo">Knotted Updo</option>
          <option value="Ballerina Bun">Ballerina Bun</option>
          <option value="Banana Clip Updo">Banana Clip Updo</option>
          <option value="Beehive">Beehive</option>
          <option value="Bouffant">Bouffant</option>
          <option value="Hair Bow">Hair Bow</option>
          <option value="Half-Up Top Knot">Half-Up Top Knot</option>
          <option value="Half-Up, Half-Down">Half-Up, Half-Down</option>
          <option value="Messy Bun with a Headband">Messy Bun with a Headband</option>
          <option value="Messy Bun with a Scarf">Messy Bun with a Scarf</option>
          <option value="Messy Fishtail Braid">Messy Fishtail Braid</option>
          <option value="Sideswept Pixie">Sideswept Pixie</option>
          <option value="Mohawk Fade">Mohawk Fade</option>
          <option value="Zig-Zag Part">Zig-Zag Part</option>
          <option value="Victory Rolls">Victory Rolls</option>
        </select>
      </div>

      <!-- Hair color -->
      <div>
        <label class="block text-sm font-medium mb-1" for="hairColor">Hair color</label>
        <select
          id="hairColor"
          v-model="form.hairColor"
          class="w-full border rounded px-3 py-3 sm:py-2 text-base sm:text-sm bg-white text-slate-900 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="No change">No change</option>
          <option value="Random">Random</option>
          <option value="Blonde">Blonde</option>
          <option value="Brunette">Brunette</option>
          <option value="Black">Black</option>
          <option value="Dark Brown">Dark Brown</option>
          <option value="Medium Brown">Medium Brown</option>
          <option value="Light Brown">Light Brown</option>
          <option value="Chestnut">Chestnut</option>
          <option value="Auburn">Auburn</option>
          <option value="Copper">Copper</option>
          <option value="Red">Red</option>
          <option value="Strawberry Blonde">Strawberry Blonde</option>
          <option value="Platinum Blonde">Platinum Blonde</option>
          <option value="Silver">Silver</option>
          <option value="White">White</option>
          <option value="Blue">Blue</option>
          <option value="Purple">Purple</option>
          <option value="Pink">Pink</option>
          <option value="Green">Green</option>
          <option value="Blue-Black">Blue-Black</option>
          <option value="Golden Blonde">Golden Blonde</option>
          <option value="Honey Blonde">Honey Blonde</option>
          <option value="Caramel">Caramel</option>
          <option value="Mahogany">Mahogany</option>
          <option value="Burgundy">Burgundy</option>
          <option value="Jet Black">Jet Black</option>
          <option value="Ash Brown">Ash Brown</option>
          <option value="Ash Blonde">Ash Blonde</option>
          <option value="Titanium">Titanium</option>
          <option value="Rose Gold">Rose Gold</option>
        </select>
      </div>

      <!-- Gender -->
      <div>
        <label class="block text-sm font-medium mb-1" for="gender">Gender <span class="text-gray-400 font-normal">(optional, helps model accuracy)</span></label>
        <select
          id="gender"
          v-model="form.gender"
          class="w-full border rounded px-3 py-3 sm:py-2 text-base sm:text-sm bg-white text-slate-900 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="none">Not specified</option>
          <option value="male">Male</option>
          <option value="female">Female</option>
        </select>
      </div>

      <div v-if="submitError" class="space-y-2">
        <div class="rounded-2xl border border-rose-400/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-100">
          {{ submitError }}
        </div>
        <button
          v-if="lastErrorWasNetwork"
          type="button"
          @click="handleSubmit"
          class="w-full rounded-2xl border border-white/10 bg-white/5 px-4 py-2 text-sm font-medium text-slate-200 transition hover:bg-white/10"
        >
          Try again
        </button>
      </div>

      <button
        type="submit"
        :disabled="isSubmitting"
        class="w-full bg-blue-600 text-white py-3 px-4 rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition text-base sm:text-sm"
      >
        {{ buttonLabel }}
      </button>
    </form>
  </main>
</template>
