import { useEffect, useRef, useState } from 'react'
import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNotify } from '@/components/feedback/toastContext'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { updateLibraryRating } from './libraryApi'
import type { LibraryItem } from './libraryApi'
import { libraryItemKey, refreshLibrary } from './libraryQueries'
import { libraryInputClass } from './LibraryStatusFields'

export function RatingEditor({ item }: { item: LibraryItem }) {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const notify = useNotify()
  const active = useRef(false)
  const writing = useRef(false)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  // Derive untouched values from saved data; a refetch must not overwrite a draft.
  const [draft, setDraft] = useState<{ value: number | null } | null>(null)
  const [validationError, setValidationError] = useState('')
  const value = draft ? draft.value : item.rating
  const pendingWrites = useIsMutating({ mutationKey: ['library-write', userId, item.id] })
    + useIsMutating({ mutationKey: ['backlog-write', userId] })
  const busy = pendingWrites > 0
  const save = useMutation({
    mutationKey: ['library-write', userId, item.id],
    mutationFn: (rating: number | null) => updateLibraryRating(item.id, rating),
    onSuccess: async saved => {
      notify('Rating updated.')
      if (active.current) {
        setDraft(null)
        client.setQueryData(libraryItemKey(userId, item.id), saved)
      }
      // Invalidation cannot recreate private cache data after logout/unmount.
      await refreshLibrary(client, userId, item.game.igdbId)
    },
    onSettled: () => { writing.current = false },
  })
  function submit() {
    if (busy || writing.current || value === item.rating) return
    if (value !== null && (!Number.isInteger(value) || value < 1 || value > 10)) {
      setValidationError('Choose a whole-number rating from 1 to 10, or Not rated.')
      return
    }
    writing.current = true
    setValidationError('')
    save.mutate(value)
  }
  return <section className="space-y-4 rounded-xl border p-4" aria-labelledby="rating-title">
    <h2 id="rating-title" className="text-lg font-semibold">Rating</h2>
    <div className="max-w-xs space-y-2">
      <label htmlFor="edit-rating" className="text-sm font-medium">Your rating</label>
      <select id="edit-rating" className={libraryInputClass} value={value ?? ''} disabled={busy}
        aria-invalid={Boolean(validationError)} aria-describedby={`rating-help${validationError || save.isError ? ' rating-error' : ''}`}
        onChange={event => {
          setDraft({ value: event.target.value === '' ? null : Number(event.target.value) })
          setValidationError(''); save.reset()
        }}>
        <option value="">Not rated</option>
        {Array.from({ length: 10 }, (_, index) => index + 1).map(rating => <option key={rating} value={rating}>{rating}/10</option>)}
      </select>
    </div>
    <Button disabled={busy || value === item.rating} onClick={submit}>{save.isPending ? 'Saving rating…' : 'Save rating'}</Button>
    {save.isSuccess && <p>Rating saved.</p>}
    {(validationError || save.isError) && <p id="rating-error" role="alert" className="text-sm text-destructive">
      {validationError || 'Could not confirm the rating was saved. Your selection is kept; please try again.'}
    </p>}
    <p id="rating-help" className="text-sm text-muted-foreground">Choose Not rated and save to clear your rating.</p>
  </section>
}
