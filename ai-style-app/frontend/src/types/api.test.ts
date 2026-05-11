import { describe, expect, it } from 'vitest'
import { TERMINAL_STATUSES } from './api'

describe('API job status constants', () => {
  it('includes terminal success and failure states', () => {
    expect(TERMINAL_STATUSES).toContain('Succeeded')
    expect(TERMINAL_STATUSES).toContain('Failed')
    expect(TERMINAL_STATUSES).toContain('TimedOut')
    expect(TERMINAL_STATUSES).toContain('Canceled')
  })
})
