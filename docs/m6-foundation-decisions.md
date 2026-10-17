# M6.1 dashboard foundation decisions

Date: 2026-10-17

Status: M6.1–M6.5 complete. Final automated checks passed and the project owner
reported successful live browser acceptance without issues. M6 is accepted.
See m6-acceptance.md for evidence and the unconfirmed optional empty-account check.
The owner subsequently requested closing the services and committing/pushing the
accepted milestone on codex/m6-dashboard; service shutdown and data-volume
preservation are recorded in m6-acceptance.md. M7 has not started.

## Baseline and scope

Reviewed from accepted M5 commit
`3b982659d3e69fa1744c8f00091bf8082c86fcbf` on `codex/m6-dashboard`.
The existing uncommitted README update was preserved without modification.
The project specification remains authoritative; Section 10.6 now records these
resolved implementation choices. They were open M6 details, not previously
agreed behavior.

The review covered the dashboard contract and roadmap, UserGame lifecycle rules,
library response types, frontend routes/navigation, and library cache invalidation.
Existing UserGame and Game data supply all required counts, ratings, statuses,
dates, covers, priorities, and backlog positions. No new entity, schema, migration,
package, framework, or endpoint beyond the agreed GET /api/v1/dashboard is needed.

## Resolved policies and rationale

1. **Whole-library statistics.** Count only the authenticated user's records,
   across all five statuses. Counts are independent of the limited previews and
   sum to totalGames. Average all non-null personal ratings regardless of status.
   This describes the full library rather than only the visible games.
2. **Absent average and rounding.** Return null for no rated games and show
   "Not rated". Otherwise round once to one decimal using midpoint rounding away
   from zero; render one decimal followed by /10. For ratings 8, 8, 8, 9, and
   null, the mean is 8.25 and the API returns 8.3. Zero would misrepresent absence
   on the 1–10 personal-rating scale.
3. **Currently Playing.** Show up to five current Playing items, ordered by
   StartedAt descending, AddedAt descending, and UserGame ID ascending. Null
   StartedAt sorts last. Existing rules normally populate it for Playing items;
   this fallback does not invent a date. Historical first-start ordering stays
   stable after note/rating edits and does not imply the latest resumed session.
4. **Up Next.** Show up to five Backlog items by stored BacklogOrder ascending.
   Priority is displayed but does not replace manual order. Preserve gaps and
   stored positions. Five items keeps both lists compact; existing full library
   and backlog pages provide access to the rest, without adding pagination.
5. **Read-only, local data.** Use the existing response fields and nullability,
   with no live IGDB dependency or persistence writes. Missing covers retain the
   existing frontend fallback. Do not return personal notes or account data.
6. **Empty and failure states.** An empty library is a successful 200 with zero
   counts, null average, and empty arrays. A nonempty library can independently
   have either list empty. Provide loading and retryable error states separately
   so an API failure is never reported as an empty library.
7. **Entry point and navigation.** Replace the protected foundation page at /
   with Dashboard and update its navigation label during M6.4. Retain existing
   post-login routing behavior, including restoration of requested protected
   locations. Cards open /library/{userGameId}; sections link to the existing
   library and backlog views. Editing remains on the established feature pages.
8. **Refresh and isolation.** Read the dashboard through one API call and a
   user-scoped query key. Invalidate it after successful additions, library PATCH
   operations, removals, and backlog reorders. Preserve the existing logout and
   account-change cache cleanup, cancellation, and late-response safeguards.
   No polling or new global-state dependency is required.

## Implementation boundaries

- M6.1: review existing data/contract and document decisions in this record and
  the normative specification. No runtime code or tests are introduced.
- M6.2: implement the owner-scoped query, response mapping, persistence read,
  and authenticated GET /api/v1/dashboard within existing layers. Keep
  controllers thin and query work bounded; do not load notes or unrelated
  metadata associations for the dashboard. Ensure statistics and previews form
  a consistent read of the current user's library.
- M6.3: add focused automated backend verification against real PostgreSQL,
  with calculation unit coverage where useful.
- M6.4: implement dashboard routing/navigation, statistics and lists, states,
  query invalidation, and focused frontend tests.
- M6.5: run regression checks and live acceptance against a known dataset,
  record results and limitations, and close M6 only after manual acceptance.

## Verification cases for later steps

- Empty library; one unrated game; one rated game; mixed five-status library.
- Null ratings excluded, ratings from every status included, all-unrated mean
  null, integer mean rendering, and midpoint/non-midpoint rounding examples.
- More than five Playing and Backlog records: previews limited to five while
  statistics include all records. Verify no duplicate records from metadata joins.
- Playing order by first-start date, deterministic ties, null-date fallback,
  stable order after personal-field edits, and historical dates after re-entry.
- Up Next follows saved order even when priorities disagree or positions have
  gaps; reorder, addition, deletion, and leaving/returning to Backlog update it.
- Separate empty subsets, nullable cover/rating/priority values, links, and
  accessible loading/empty/error/retry rendering.
- Unauthorized requests return 401; two-user reads remain isolated, and a client
  cannot choose another owner. No upstream IGDB calls or database writes occur.
- Successful mutations refresh the dashboard when revisited; logout/account
  changes and late responses cannot expose stale private data.
- Manual acceptance compares counts, rounding, previews, and post-mutation
  refresh with a known persisted dataset, including unrated and reordered games.

## M6.1 verification and limits

Only the specification's dashboard policies/open-decision row and this record
were changed for M6.1. Repository diff and whitespace checks were performed.
No builds, automated test suites, migrations, or live browser acceptance were run
for this documentation-only step. Previously passing M5 checks remain historical
evidence, not new M6 execution. No services were started, no database data was
changed, and no commit/push was performed.

## M6.2 implementation and verification

Implemented on codex/m6-dashboard using the established layers:

- Application defines dashboard response records, a narrow IDashboardQueryStore
  interface, and GetDashboardHandler. The handler applies decimal rounding once
  with MidpointRounding.AwayFromZero.
- Infrastructure aggregates full-library counts and the nullable average in
  PostgreSQL, then projects at most five items per preview. All three queries
  scope ownership and use one repeatable-read transaction so concurrent writes
  cannot produce mismatched statistics and lists. Playing sorts null dates last,
  followed by the agreed date/ID ordering; Up Next preserves stored positions.
- API exposes authenticated GET /api/v1/dashboard through a thin controller,
  derives the owner from the validated sub claim, and propagates cancellation.
  Both layers register the new handler/store through existing DI entry points.
- The query does not load full entities, notes, genre/platform associations, or
  call IGDB. No schema, migration, dependency, or frontend changes were needed.

Three focused HTTP/PostgreSQL tests were included with endpoint implementation
to verify provider translation and DI wiring, unauthorized access, an empty owner,
mixed-status summaries, nullable preview fields, stored backlog gaps, rounding
8.25 to 8.3, and isolation from another owner. These use the existing isolated
database fixture; they do not substitute for M6.3's expanded edge-case coverage.

Backend build passed with zero warnings and errors. All 282 backend tests passed
(42 unit and 240 integration), with no skips. Git whitespace validation passed.
Temporary test databases were removed. PostgreSQL was temporarily started for
verification and stopped afterward without removing its persistent volume. No
development-library data was changed by the dashboard tests. API and frontend
development servers were not started, and no commit/push was performed.

At the end of M6.2, M6.3 still needed the planned coverage of preview limits, deterministic ties,
historical transitions, all-unrated/single-game variants, additional rounding
boundaries, snapshot consistency under concurrent edits, and read-only/upstream
independence checks. No M6 live browser acceptance has been claimed.

## M6.3 expanded backend verification

Expanded DashboardTests from three to 25 HTTP/PostgreSQL cases (22 additional
cases including theory rows). Existing production backend behavior passed without
requiring changes. This step changed only DashboardTests and this record; prior
README, specification, and M6.2 implementation work was preserved.

Coverage now includes:

- Signed tokens with absent, malformed, or empty owner claims are rejected.
  Two owners sharing a Game receive independent statistics and previews; a
  client-supplied userId query parameter cannot select another owner's data.
- Single rated/unrated and all-unrated libraries, lower/upper rating bounds,
  null exclusion, and decimal rounding at midpoint and non-midpoint boundaries.
  Statistics cover every status and are independent of preview limits.
- Seven Playing plus seven Backlog items produce five-item previews and counts
  covering all fourteen. Multiple metadata associations do not duplicate games.
  Nullable fields and populated covers/priorities remain correctly represented.
- Playing order honors first-start date, AddedAt and ID tie-breakers, and null
  starts last. Rating/note updates and leaving/resuming Playing do not promote
  an item or replace its historical StartedAt.
- Nonempty libraries can have either or both previews empty. Up Next respects
  stored order and gaps even when priority disagrees. Reorders, leaving/returning
  to Backlog, deletion, addition, priority edits, rating updates, and rating clears
  are visible on subsequent reads while the other owner's data stays unchanged.
- A throwing IGDB test double confirms dashboard requests need no live provider.
  A command interceptor rejects writes and inspects the dashboard SELECTs for
  excluded note and metadata-association columns; stored personal fields and
  lifecycle timestamps remain unchanged.
- A coordinated concurrency test pauses the request after its first database
  read, commits a status/rating change from another connection, then releases the
  previews. The in-flight response retains the original statistics and lists;
  the next request sees the committed state. Synchronization uses bounded signals
  rather than sleeps and runs against real PostgreSQL.

Verification: backend build passed with zero errors and warnings; the full backend
suite passed all 304 tests (42 unit and 262 integration), with no skips. All 25
dashboard cases passed as part of that run. Git whitespace checks passed.

Fixtures applied committed migrations to their own temporary databases and
removed them afterward; a final check found zero remaining fixture databases.
PostgreSQL was stopped afterward with its persistent volume preserved. Dashboard
test data stayed in isolated databases; no development-library records were
changed. API/frontend development servers were not started. Frontend tests and
live M6 acceptance were not run, as frontend implementation is M6.4 scope.
No commit/push was performed and M6.4 has not started.

## M6.4 dashboard frontend

The protected / route now renders DashboardPage instead of FoundationPage; the
unused foundation component was removed and navigation renamed to Dashboard.
The page uses one authenticated /dashboard request with a user-scoped query key
and an AbortSignal. It displays whole-library counts, nullable/one-decimal average,
Currently Playing ratings, and Up Next priorities and stored positions. Server
list order is preserved. Existing cover fallback and library-item links are reused;
full-list links open the Playing library filter and backlog. No editing controls
or new packages were added.

The layout uses responsive statistics and preview grids and allows the longer
navigation label to wrap. Accessible status/alert messages distinguish loading,
empty library, independently empty previews, initial failure, and a failed refresh
with retained data. Both failure states offer retry without inventing zero counts.
Average formatting is presentation only; calculation/rounding remains server-side.

Shared library invalidation now includes the owner's dashboard after additions,
status/priority changes, rating/note saves, and removal. Successful backlog reorder
also invalidates it. Invalidation does not create cache entries after cleanup.
Existing auth cache clearing and query cancellation protect account transitions
and late responses.

A new sign-in regression test exposed a redirect race: GuestPage could navigate
to / as authentication changed before LoginPage restored its requested location.
GuestPage now respects that same requested location, with / as its default. This
preserves the M6.1 navigation policy; auth token/cookie behavior was not changed.

Added 14 dashboard frontend cases and one requested-location auth case, and
extended existing mutation tests to verify dashboard invalidation for every write
path. Coverage includes server ordering, complete counts, links, populated/missing
and failed covers, null/integer averages, empty/error/retry states, retained-data
refresh errors, owner-key changes, actual logout cache clearing, StrictMode
cancellation, ignored late responses, and navigating back after a rating save.
The navigation-back test uses an otherwise perpetually fresh query to ensure
the save really invalidates it.

All 85 frontend tests passed (70 baseline plus 15 additional cases). Production
TypeScript/Vite build, lint, and Git whitespace checks passed. Backend tests were
not rerun for this frontend-only step; the 304-test M6.3 result remains the prior
backend baseline. API, frontend development server, and PostgreSQL were not
started. Real browser/layout acceptance and known-dataset comparison remain
M6.5 work, not claimed passes. No schema, dependency, or production backend files
changed in M6.4; prior README/specification/backend work was preserved.
No commit/push was performed. M6.5 has not started.

## M6.5 acceptance completion

Final backend/frontend regression, builds, lint, dependency audit, EF model check,
repository audits, development integrity, and temporary-database cleanup passed.
API, frontend, and PostgreSQL were started for owner acceptance. A read-only check
recorded anonymized counts/averages for the existing libraries for comparison.
The requested browser scenario and automated results are in m6-acceptance.md.
The owner confirmed that everything appeared to work properly with no issues;
the main scenario is accepted. The optional empty-account live check was not
explicitly confirmed and is not recorded as passed. No code changes were needed
after this confirmation. Services were left running; no commit/push or M7 work
was performed.
