import { useState } from 'react'

export function GameCover({ url, name }: { url: string | null; name: string }) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  return <div className="flex aspect-[3/4] items-center justify-center overflow-hidden rounded-lg bg-muted text-muted-foreground">
    {url && failedUrl !== url
      ? <img src={url} alt={`${name} cover`} loading="lazy" className="h-full w-full object-cover" onError={() => setFailedUrl(url)} />
      : <span className="p-6 text-center text-sm">Cover unavailable</span>}
  </div>
}
