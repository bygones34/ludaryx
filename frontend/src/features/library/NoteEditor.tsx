import { useEffect, useRef, useState } from 'react'
import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNotify } from '@/components/feedback/toastContext'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { updateLibraryNote } from './libraryApi'
import type { LibraryItem } from './libraryApi'
import { libraryItemKey, refreshLibrary } from './libraryQueries'
import { libraryInputClass } from './LibraryStatusFields'

export function NoteEditor({ item }: { item: LibraryItem }) {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const notify = useNotify()
  const active = useRef(false)
  const writing = useRef(false)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const [draft, setDraft] = useState<{ value: string } | null>(null)
  const value = draft ? draft.value : item.personalNote ?? ''
  const changed = value !== (item.personalNote ?? '')
  const tooLong = value.length > 1000
  const pendingWrites = useIsMutating({ mutationKey: ['library-write', userId, item.id] })
    + useIsMutating({ mutationKey: ['backlog-write', userId] })
  const busy = pendingWrites > 0
  const save = useMutation({
    mutationKey: ['library-write', userId, item.id],
    mutationFn: (note: string | null) => updateLibraryNote(item.id, note),
    onSuccess: async saved => {
      notify('Note updated.')
      if (active.current) {
        setDraft(null)
        client.setQueryData(libraryItemKey(userId, item.id), saved)
      }
      await refreshLibrary(client, userId, item.game.igdbId)
    },
    onSettled: () => { writing.current = false },
  })
  function submit(note: string | null) {
    if (busy || writing.current || (note?.length ?? 0) > 1000 || note === item.personalNote) return
    writing.current = true
    save.mutate(note)
  }
  return <section className="space-y-4 rounded-xl border p-4" aria-labelledby="note-title">
    <h2 id="note-title" className="text-lg font-semibold">Personal note</h2>
    <div className="space-y-2">
      <label htmlFor="edit-note" className="text-sm font-medium">Your personal note</label>
      <textarea id="edit-note" className={`${libraryInputClass} min-h-32 resize-y`} rows={5} value={value}
        disabled={busy} aria-invalid={tooLong} aria-describedby={`note-count note-help${tooLong ? ' note-length-error' : ''}${save.isError ? ' note-save-error' : ''}`}
        onChange={event => { setDraft({ value: event.target.value }); save.reset() }} />
      <p id="note-count" className="text-sm text-muted-foreground">{value.length}/1,000 characters</p>
      <p id="note-help" className="text-sm text-muted-foreground">Plain text, up to 1,000 characters. Saving an empty note clears it. Notes are saved only when you choose Save note.</p>
    </div>
    <div className="flex flex-wrap gap-3">
      <Button disabled={busy || !changed || tooLong} onClick={() => submit(value === '' ? null : value)}>{save.isPending ? 'Saving note…' : 'Save note'}</Button>
      <Button variant="outline" disabled={busy || item.personalNote === null} onClick={() => {
        setDraft({ value: '' }); submit(null)
      }}>Clear note</Button>
      <Button variant="outline" disabled={busy || draft === null} onClick={() => { setDraft(null); save.reset() }}>Discard note changes</Button>
    </div>
    {save.isSuccess && <p>Note saved.</p>}
    {tooLong && <p id="note-length-error" role="alert" className="text-sm text-destructive">The note exceeds 1,000 characters. Shorten it before saving; your text has been kept.</p>}
    {save.isError && <p id="note-save-error" role="alert" className="text-sm text-destructive">Could not confirm the note was saved. Your text is kept; please try again.</p>}
  </section>
}
