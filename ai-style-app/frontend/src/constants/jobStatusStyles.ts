import type { JobStatus } from '@/types/api'

const DEFAULT_LIGHT_PILL = 'bg-gray-100 text-gray-800'
const DEFAULT_DARK_PILL = 'bg-slate-500/15 text-slate-200 border-slate-400/20'

const LIGHT_STATUS_PILL_CLASS: Record<JobStatus, string> = {
  Queued: 'bg-yellow-100 text-yellow-800',
  Processing: 'bg-blue-100 text-blue-800',
  Succeeded: 'bg-green-100 text-green-800',
  Failed: 'bg-red-100 text-red-800',
  TimedOut: 'bg-gray-100 text-gray-800',
  Canceled: 'bg-gray-100 text-gray-800'
}

const DARK_STATUS_PILL_CLASS: Record<JobStatus, string> = {
  Queued: 'bg-amber-500/15 text-amber-200 border-amber-400/20',
  Processing: 'bg-sky-500/15 text-sky-200 border-sky-400/20',
  Succeeded: 'bg-emerald-500/15 text-emerald-200 border-emerald-400/20',
  Failed: 'bg-rose-500/15 text-rose-200 border-rose-400/20',
  TimedOut: 'bg-slate-500/15 text-slate-200 border-slate-400/20',
  Canceled: 'bg-slate-500/15 text-slate-200 border-slate-400/20'
}

export function getLightJobStatusPillClass(status: JobStatus) {
  return LIGHT_STATUS_PILL_CLASS[status] ?? DEFAULT_LIGHT_PILL
}

export function getDarkJobStatusPillClass(status: JobStatus) {
  return DARK_STATUS_PILL_CLASS[status] ?? DEFAULT_DARK_PILL
}
