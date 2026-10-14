# M4 backlog acceptance record

Date: 2026-10-14

Status: Complete. Automated verification passed and the project owner confirmed
the live ten-game browser acceptance of persistence and status transitions.

## Final automated checks

| Check | Result |
|---|---|
| Backend build | Passed; 0 errors and 0 warnings |
| Backend tests | Passed; 268 tests (42 unit and 226 integration), no skips |
| Frontend tests | Passed; 53 tests |
| Frontend production build and lint | Passed |
| Frontend dependency audit | Passed; 0 reported vulnerabilities |
| PostgreSQL readiness | Passed; local container healthy |
| HTTPS API health | Passed; HTTP 200, Healthy |
| EF Core model against latest migration | No pending model changes |
| Backlog uniqueness constraint | Deferrable, initially immediate |
| Development database integrity | No duplicate owner/order groups or invalid backlog fields |
| Temporary integration databases | None remain after the test suite |
| Git whitespace check | Passed |
| Repository secret check | No configured secret values found; no generated/local-only files tracked or unignored |

Backend tests use isolated real PostgreSQL databases and verify owned ordered
retrieval, full-list validation, append/re-entry, status transitions, priority
clearing, preservation of personal fields, rollback, concurrent reorders, and
database constraint enforcement. They do not populate the development library.
At the read-only integrity check, the development database held two Games and
two Backlog UserGames. No new migration was created during M4.4.

Frontend tests cover protected routes, server-authoritative reorder results,
priority-only updates, pending writes, retry/rollback, conflict reload, cache
invalidation, and account changes. See m4-frontend-verification.md for the earlier
browser fixture checks; those are separate from live acceptance.

## Live acceptance results

The local services are available at https://localhost:5173 (frontend) and
https://localhost:7034 (API), with PostgreSQL on localhost:5432.

The project owner confirmed:

- Ten games were added to Backlog and reordered.
- Reloading preserved saved order and priorities.
- Moving a game to Playing removed it from the backlog.
- Returning that game to Backlog appended it at the end without a priority.

Pointer and keyboard drag, cancellation, and Move down controls were separately
verified with the real UI in the M4.3 browser fixture. The owner's live report
confirms reordered persistence without distinguishing the input method used.

The final read-only PostgreSQL check found eleven Games and eleven Backlog
UserGames, with ten records for one owner and one for another. No duplicate
owner/order groups or invalid backlog fields were found. Persistent positions
may contain gaps after status changes, as approved; a later explicit reorder
compacts them. This final snapshot verifies current integrity, not the historical
Playing transition; the owner report and automated transition tests provide that
evidence. Automated owner-isolation and atomicity tests also passed.

No new migration, commit, push, merge, or subsequent milestone was performed in
M4.4. After acceptance, the project owner requested committing and pushing M4
and stopping the API, frontend, and PostgreSQL. The services were stopped without
removing the persistent Docker volume.
