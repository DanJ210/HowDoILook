import { describe, expect, it, vi } from 'vitest'
import { sharePrimaryResult } from './primaryResultActions'

describe('sharePrimaryResult', () => {
  it('uses the native share API when available', async () => {
    const share = vi.fn().mockResolvedValue(undefined)

    await expect(sharePrimaryResult('https://example.com/result.webp', { share })).resolves.toBe('shared')
    expect(share).toHaveBeenCalledWith({
      title: 'My automatic style result',
      text: 'Take a look at my automatic style result.',
      url: 'https://example.com/result.webp'
    })
  })

  it('copies the image URL when native sharing is unavailable', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)

    await expect(sharePrimaryResult('https://example.com/result.webp', {
      clipboard: { writeText }
    })).resolves.toBe('copied')
    expect(writeText).toHaveBeenCalledWith('https://example.com/result.webp')
  })

  it('rejects when the browser has no supported share mechanism', async () => {
    await expect(sharePrimaryResult('https://example.com/result.webp', {}))
      .rejects.toThrow('Sharing is not supported by this browser.')
  })
})