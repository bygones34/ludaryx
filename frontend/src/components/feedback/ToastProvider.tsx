import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Check, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ToastContext } from './toastContext'
import type { SuccessMessage } from './toastContext'

type Toast = { id: number; message: SuccessMessage }

// App keys this provider by session scope. Old async callbacks cannot publish into
// its replacement, including when the next sign-in belongs to the same account.
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<Toast | null>(null)
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = useRef(false)
  const sequence = useRef(0)
  useLayoutEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const notify = useCallback((message: SuccessMessage) => {
    if (active.current) setToast({ id: ++sequence.current, message })
  }, [])
  const dismiss = useCallback((id: number) => {
    setToast(current => current?.id === id ? null : current)
    setHovered(false); setFocused(false)
  }, [])
  return <ToastContext.Provider value={notify}>
    {children}
    <section aria-label="Notifications" className="pointer-events-none fixed inset-x-4 bottom-4 z-50 sm:left-auto sm:w-96">
      <div className={toast ? 'pointer-events-auto rounded-xl border border-primary/30 bg-card text-card-foreground shadow-lg' : ''}
        onMouseEnter={() => setHovered(true)} onMouseLeave={() => setHovered(false)}
        onFocusCapture={() => setFocused(true)} onBlurCapture={event => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setFocused(false)
        }}>
        {/* Keep the polite region mounted; only the success text is announced. */}
        <div aria-live="polite" aria-atomic="true">
          {toast && <p className="flex items-center gap-3 px-4 pt-4"><Check aria-hidden="true" className="size-5 shrink-0 text-primary" />{toast.message}</p>}
        </div>
        {toast && <ToastLifetime toast={toast} dismiss={dismiss} paused={hovered || focused} />}
      </div>
    </section>
  </ToastContext.Provider>
}

function ToastLifetime({ toast, dismiss, paused }: { toast: Toast; dismiss: (id: number) => void; paused: boolean }) {
  const remaining = useRef(5000)
  const currentId = useRef(toast.id)
  useEffect(() => {
    if (currentId.current !== toast.id) { remaining.current = 5000; currentId.current = toast.id }
    if (paused) return
    const started = Date.now()
    const timer = setTimeout(() => dismiss(toast.id), remaining.current)
    return () => { clearTimeout(timer); remaining.current = Math.max(0, remaining.current - (Date.now() - started)) }
  }, [paused, toast.id, dismiss])
  return <div className="flex justify-end px-3 pb-2 pt-1">
    <Button variant="ghost" aria-label="Dismiss notification" onClick={() => dismiss(toast.id)}><X aria-hidden="true" /></Button>
  </div>
}
