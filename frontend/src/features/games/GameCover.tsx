import { useState } from 'react'
import { ImageOff } from 'lucide-react'

export function GameCover({ url, name, compact = false }: { url: string | null; name: string; compact?: boolean }) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  return <div className="flex aspect-[3/4] items-center justify-center overflow-hidden rounded-lg bg-muted text-muted-foreground">
    {url && failedUrl !== url
      ? <img src={url} alt={`${name} cover`} loading="lazy" className="h-full w-full object-cover" onError={() => setFailedUrl(url)} />
      : <span className="flex max-w-full flex-col items-center gap-2 p-2 text-center text-sm">
        <ImageOff aria-hidden="true" className="size-5 shrink-0 opacity-60" />
        <span className={compact ? 'sr-only' : ''}>Cover unavailable</span>
      </span>}
  </div>
}
