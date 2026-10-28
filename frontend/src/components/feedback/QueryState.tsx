import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'

export function LoadingState({ message, layout = 'cards' }: {
  message: string; layout?: 'cards' | 'details' | 'rows' | 'dashboard' | 'compact'
}) {
  return <div className="space-y-4">
    <p role="status" className="text-sm text-muted-foreground">{message}</p>
    {layout !== 'compact' && <div aria-hidden="true" className={layout === 'details'
      ? 'grid gap-6 md:grid-cols-[200px_minmax(0,1fr)]'
      : layout === 'rows' ? 'space-y-3' : layout === 'dashboard' ? 'grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4'
        : 'grid grid-cols-1 gap-6 min-[480px]:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'}>
      {Array.from({ length: layout === 'details' ? 2 : layout === 'dashboard' ? 7 : 3 }, (_, index) =>
        <div key={index} className={`rounded-xl border bg-card p-4 motion-safe:animate-pulse ${layout === 'cards' ? 'space-y-4' : 'space-y-3'}`}>
          {layout === 'cards' && <div className="aspect-[3/4] max-h-64 rounded-lg bg-muted" />}
          {layout === 'details' && index === 0 && <div className="aspect-[3/4] max-h-72 rounded-lg bg-muted" />}
          <div className="h-4 w-3/4 rounded bg-muted" />
          <div className="h-4 w-1/2 rounded bg-muted" />
        </div>)}
    </div>}
  </div>
}

export function EmptyState({ title, description, children }: { title: string; description?: string; children?: ReactNode }) {
  return <div className="space-y-3 rounded-xl border bg-muted/40 p-6">
    <p className="font-medium">{title}</p>
    {description && <p className="text-sm text-muted-foreground">{description}</p>}
    {children}
  </div>
}

export function ErrorState({ message, retry, retrying = false, children }: {
  message: string; retry?: () => void; retrying?: boolean; children?: ReactNode
}) {
  return <div role="alert" className="space-y-3 rounded-xl border border-destructive/40 bg-card p-6">
    <p>{message}</p>
    {retry && <Button variant="outline" disabled={retrying} onClick={retry}>Try again</Button>}
    {children}
  </div>
}
