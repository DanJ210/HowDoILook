export type PrimaryResultShareTarget = {
  share?: (data: ShareData) => Promise<void>
  clipboard?: {
    writeText: (text: string) => Promise<void>
  }
}

export type PrimaryResultShareOutcome = 'shared' | 'copied'

export async function sharePrimaryResult(
  imageUrl: string,
  target: PrimaryResultShareTarget = navigator
): Promise<PrimaryResultShareOutcome> {
  if (target.share) {
    await target.share({
      title: 'My automatic style result',
      text: 'Take a look at my automatic style result.',
      url: imageUrl
    })
    return 'shared'
  }

  if (target.clipboard) {
    await target.clipboard.writeText(imageUrl)
    return 'copied'
  }

  throw new Error('Sharing is not supported by this browser.')
}