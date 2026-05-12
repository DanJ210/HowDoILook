import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/pages/HomePage.vue')
  },
  {
    path: '/style/generate',
    name: 'style-generate',
    component: () => import('@/pages/StyleGeneratePage.vue')
  },
  {
    path: '/jobs',
    name: 'jobs',
    component: () => import('@/pages/JobsPage.vue')
  },
  {
    path: '/jobs/:id',
    name: 'job-status',
    component: () => import('@/pages/JobStatusPage.vue')
  },
  {
    path: '/account',
    name: 'account',
    component: () => import('@/pages/AccountPage.vue')
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/pages/NotFoundPage.vue')
  }
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

export default router

