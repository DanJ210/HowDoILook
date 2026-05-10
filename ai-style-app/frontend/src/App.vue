<script setup lang="ts">
import { RouterView } from 'vue-router'
import BottomNav from '@/components/BottomNav.vue'
import AuthProfileControl from '@/components/AuthProfileControl.vue'
</script>

<template>
  <div class="min-h-dvh bg-slate-950 text-slate-100">
    <div class="min-h-dvh bg-[radial-gradient(circle_at_top,_rgba(56,189,248,0.15),_transparent_35%),linear-gradient(180deg,_#0f172a_0%,_#0b1220_45%,_#020617_100%)] pb-24 sm:pb-28">
      <AuthProfileControl />
      <RouterView />
    </div>
    <BottomNav />
  </div>
</template>

<style scoped>
</style>
