# M6 dashboard acceptance

Date: 2026-10-17

Status: Complete. Automated verification passed and the project owner reported
that live acceptance was working properly with no issues. M6.5 and M6 are accepted,
subject to the explicitly recorded optional-check limitation below.

Branch: codex/m6-dashboard. Implementation decisions and step-specific evidence
are recorded in m6-foundation-decisions.md. Milestone closure uses this branch;
no PR, merge, or M7 work is included.

## Final automated verification

| Check | Result |
|---|---|
| Backend build | Passed; zero errors and warnings |
| Backend tests | Passed; 304 (42 unit, 262 integration), no skips |
| Dashboard HTTP/PostgreSQL cases | All 25 passed within the backend suite |
| Frontend tests | Passed; 85 |
| Frontend production TypeScript/Vite build | Passed |
| Frontend lint | Passed |
| Frontend dependency audit | Zero reported vulnerabilities |
| EF Core model compared with latest migration | No pending model changes |
| PostgreSQL readiness | Healthy |
| HTTPS API health | HTTP 200, Healthy |
| HTTPS frontend | HTTP 200 |
| Development library integrity | Zero invalid rating/note/backlog-field records |
| Temporary integration-test databases | Zero remain |
| Configured-secret audit | Zero matches in tracked/unignored repository files |
| Generated/local-only file audit | Zero tracked/unignored matches |
| Git whitespace check | Passed |

No new migration or dependency was required for M6. Integration fixtures apply
committed migrations to isolated temporary databases and remove those databases
afterward. Dashboard automated requests use a throwing IGDB double; no live IGDB
request is needed to render persisted summaries.

Backend coverage includes empty/unrated/single/mixed libraries, full counts,
decimal rounding, bounded lists, ties/null dates and historical start dates,
stored backlog order, owner isolation, mutation visibility, read-only projections,
and consistent snapshots during a concurrent committed edit.

Frontend coverage includes statistics and one-decimal/null display, list order,
links and cover fallback, empty/loading/error/retry states, refresh invalidation
for library/backlog writes, navigation back after a save, protected navigation,
auth cache clearing, cancellation, and late responses. These are automated tests
with controlled responses; they do not establish real browser layout acceptance.

## Read-only development dataset baseline

Before live acceptance, the development database held eleven shared Games and
eleven UserGames across two nonempty libraries. No account identifiers, account
details, credentials, or personal note contents were retrieved for this check.

| Existing library | Total | Playing | Completed | Backlog | On hold | Dropped | Rated games | Average |
|---|---|---|---|---|---|---|---|---|
| Ten-game library | 10 | 0 | 1 | 9 | 0 | 0 | 1 | 9.0 |
| One-game library | 1 | 0 | 0 | 1 | 0 | 0 | 0 | null / Not rated |

This is a persisted starting-state comparison aid, not a report that the browser
display was verified. Owner-driven acceptance edits may change these values.
Verification queries ran in a read-only transaction and made no development-data
changes. The automated tests wrote only to their isolated fixture databases.

## Live browser acceptance

API and frontend were started at https://localhost:7034 and
https://localhost:5173; PostgreSQL was running for the project owner's checks.
Requested checks:

1. Sign in; compare dashboard counts/average with the known library. Verify
   Currently Playing is initially empty and Up Next contains the first five
   saved backlog entries (or the single entry in the smaller library).
2. Open a game, move it to Playing, save a rating, and return through Dashboard.
   Confirm counts/average/previews refresh. Exercise both full-list links.
3. Reorder the backlog and verify Up Next follows it. Add a game, then remove
   that newly added game and confirm totals update after each operation.
4. Reload and logout/login; confirm the saved summary persists. Inspect a narrow
   or mobile-sized window for readable cards and usable navigation.
5. If an empty account/library is available, verify zero counts, Not rated, and
   the empty messages. Otherwise record this live check as unperformed; automated
   coverage alone must not be presented as live verification.

The project owner replied to this checklist: "I did not encounter any issues so
far. All seems to be working properly." This is recorded as owner acceptance of
the main browser scenario (checks 1–4); no browser failures were reported.
The response did not individually enumerate results. In particular, the optional
empty-account check was not explicitly confirmed and is not claimed as a live
pass. Empty-library behavior has passing backend and frontend automated coverage.

No additional code or test rerun was needed to record acceptance. The automated
results above remain the final M6.5 run. API, frontend, and PostgreSQL were left
running after acceptance; no shutdown, commit, push, PR, or merge was performed.
The previously documented Edge fetch-cancellation issue is not claimed resolved
by this milestone. No M7 work has begun.

## Milestone closure

After acceptance, the project owner requested service shutdown, documentation
updates, and committing/pushing the accepted M6 deliverables to codex/m6-dashboard.
API and frontend listeners are closed; PostgreSQL is stopped. The persistent
database volume was preserved. README now reflects accepted M0–M6 features and
the 304-backend/85-frontend test baseline. No additional runtime changes or test
reruns were needed for these documentation and Git finalization actions.

The existing Edge AbortError also appeared in the frontend session logs retrieved
at shutdown. It remains unresolved; the owner reported no functional acceptance
failure. No PR, merge, or M7 implementation is part of this closure.
