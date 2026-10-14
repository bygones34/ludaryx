# M5.1 ratings and personal notes foundation

Date: 2026-10-14

Reviewed on feature/m5-ratings-personal-notes, created from the accepted M4 commit
db1cffd0f3eab9f81e6fd368e3635203d7272af0. The project specification remains the
source of truth. This record does not introduce a new API or schema contract.

## Existing foundation

- UserGame already stores nullable Rating, PersonalNote, StartedAt, and CompletedAt.
- PATCH /api/v1/library/{id} accepts rating and personalNote independently.
  Omitted fields remain unchanged; explicit null clears nullable fields.
  Unsupported fields, including dates, are rejected.
- Application and domain validation accept null or integer ratings 1 through 10
  and notes of at most 1,000 characters. The database also enforces these bounds.
- First entry into Playing sets StartedAt; first entry into Completed sets
  CompletedAt. Subsequent transitions preserve both historical dates. Completing
  directly does not invent a Playing date. Dates are server-managed and stored UTC.
- Mutations are owner-scoped and serialized using the existing user-row lock.
- Details already display rating, note, and lifecycle dates. The frontend update
  helper currently only sends status and priority; there are no rating/note editors.

## Implementation choices for the remaining steps

1. Reuse the existing library PATCH and detail response. No new endpoint, package,
   entity, or migration is currently needed.
2. Provide a native rating selector with Not rated and integer values 1 through 10.
   Use an explicit Save action, consistent with the existing detail status editor.
   Send only rating; clearing sends null.
3. Provide a plain-text multiline note editor with a length counter, explicit Save,
   and a clear action that sends null. Preserve nonempty text, whitespace, and line
   breaks; do not silently trim or rewrite it. Client length checks use JavaScript
   string length, matching the existing .NET string-length validation.
4. Keep rating/note saves separate from status/priority changes. Coordinate pending
   writes for the owned item and refresh the existing owner-scoped caches. Preserve
   unsaved text on errors; background refresh or another field's save must not
   silently discard a note draft. Late responses after logout must not recreate
   private data in the cache.
5. Keep lifecycle dates read-only. Reuse the existing display and improve labels
   or explanations only as needed to make first-transition semantics clear.

## Verification gaps to address in M5.2

Existing tests already cover invalid ratings, nullable clears, omitted-field
preservation, 1,000/1,001-character notes, database bounds, repeated domain
transitions, ownership, and concurrent independent PATCH updates.

Add focused cases only where coverage is incomplete: both rating bounds through
PATCH followed by GET; rejection of StartedAt/CompletedAt edits; direct completion
and repeated completion through HTTP; note-only updates preserving dates and other
fields; multiline/Unicode note round-trips and failed combined updates leaving the
entire record unchanged. Reuse existing isolated PostgreSQL fixtures.

Frontend tests in M5.3/M5.4 should cover editing, clearing, exact partial payloads,
validation, pending writes, failed-save draft retention, successful cache refresh,
and account/navigation changes.

## Step boundaries

- M5.1: static review and this foundation record; no runtime implementation.
- M5.2: focused backend contract and lifecycle verification, fixes only if needed.
- M5.3: rating editing and related frontend tests.
- M5.4: note editing and lifecycle presentation, with related frontend tests.
- M5.5: complete regression checks and live acceptance: Backlog to Playing, rating
  9, add note, Completed, then logout/login and verify persistence.

At completion of M5.1, services remained stopped and no tests were run; the
passing M4 suite was the baseline, not evidence of new M5 execution.

## M5.2 verification results

Eleven additional HTTP/PostgreSQL cases were added to LibraryManagementTests,
reusing its isolated database fixture and an injectable test-only clock. They
cover rating 1/10/null round-trips; rejected client edits to both lifecycle dates;
multiline and Unicode notes, including the existing UTF-16 length boundary;
atomic rejection of invalid combined edits; repeated lifecycle transitions at
distinct times; and direct completion without an invented start date.

All 279 backend tests passed (42 unit, 237 integration), with no skips. Backend
build passed with zero errors and warnings. The focused LibraryManagementTests
run passed all 73 cases. No production code, endpoint, package, or migration
change was needed. Git whitespace validation passed.

After testing, no temporary fixture databases remained. Read-only checks found
the same eleven Games and eleven UserGames in the development database. Only
PostgreSQL was temporarily started for verification and was stopped afterward;
the API and frontend were not started. M5.3 has not begun.

## M5.3 rating editor results

The library detail now includes an explicitly saved rating selector: Not rated
or integers 1 through 10. Its PATCH contains only rating; selecting Not rated
and saving sends null. The saved detail uses the server response and related
owner-scoped library/backlog caches are invalidated.

The selector preserves unsaved choices across detail refreshes and status saves.
Failed writes retain the selection for retry. Pending rating writes disable the
selector and competing status/removal actions; a synchronous guard prevents
duplicate submissions. Late responses after unmount do not repopulate private
cache entries after logout cleanup.

Eight new frontend cases cover rating bounds and clearing, partial payloads,
cache refresh, pending-write controls, failed-save retry, draft preservation,
untouched values following refreshed data, and late-response cleanup. All 61
frontend tests passed. Production TypeScript/Vite build, lint, and Git whitespace
validation passed. No new dependencies or backend changes were needed in M5.3.

At completion of M5.3, live browser acceptance remained M5.5, services remained
stopped, and M5.4 note editing had not started. No M5 commit or push was made.

## M5.4 note editor and lifecycle presentation results

The personal note section now provides a labeled plain-text multiline editor,
UTF-16 length counter, explicit Save note, Clear note, and Discard note changes.
Empty saves and Clear note send personalNote null. Nonempty text is not trimmed;
Unicode, whitespace, and line breaks are preserved. Oversized text is kept visible
with an accessible error and cannot be saved; it is not silently truncated.

Only personalNote is included in note PATCH requests. Pending writes disable
competing item edits and removal. Failed saves retain the draft for retry.
Unsaved notes survive successful rating/status changes and failed background
detail refreshes. Untouched editors use current saved data. Late unmounted saves
invalidate existing keys without recreating private cached note data.

Started and Completed remain read-only and the detail explains first-transition
timestamp preservation. No date editor or date field is sent from these controls.
The shared rating/note test file is named LibraryPersonalFields.test.tsx.

Nine new note/lifecycle frontend cases bring the passing total to 70 tests.
Production build and lint pass; Git whitespace validation passes. No backend,
schema, endpoint, or dependency changes were needed for M5.4. Services remain
stopped, no commit/push was made, and M5.5 live acceptance has not begun.

## M5.5 completion

Final regression checks passed: 279 backend tests, 70 frontend tests, backend and
frontend builds, lint, dependency audit, model/migration comparison, secret audit,
and Git whitespace validation. The project owner confirmed the entire live
acceptance scenario without problems, including logout/login persistence and
unsaved-note retention. A final read-only database integrity check also passed.
See m5-acceptance.md for the complete evidence and its limits. M5 is complete;
no commit/push or M6 implementation has been performed.
