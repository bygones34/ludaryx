# M3 my library acceptance record

Date: 2026-10-12

Status: Complete. Automated checks passed and the project owner confirmed the
live browser acceptance of isolation, updates, list filtering/sorting, and deletion.

## Automated checks

| Check | Result |
|---|---|
| Backend build | Passed; 0 errors and 0 warnings |
| Backend tests | Passed; 226 tests (34 unit and 192 integration), no skips |
| Frontend tests | Passed; 34 tests |
| Frontend production build and lint | Passed |
| Frontend dependency audit | Passed; 0 reported vulnerabilities |
| PostgreSQL readiness | Passed; local container healthy |
| API health over HTTPS | Passed; HTTP 200 with Healthy |
| EF Core model compared with latest migration | No pending model changes |
| Git whitespace check | Passed |
| Repository secret check | No matches for configured local secret values; .env is ignored and untracked |
| Local test database cleanup | No residual ludaryx_m3_test_* databases |

Integration tests use isolated PostgreSQL databases and controlled IGDB responses.
They cover snapshot reuse, metadata uniqueness, concurrent additions and updates,
transaction rollback, duplicate ownership, owner isolation, validation, list
filters, all supported sorts, pagination, partial updates, lifecycle timestamps,
deletion without removing shared metadata, and discovery membership.

Frontend tests cover protected routes, addition and duplicate recovery, owner-scoped
cache behavior, named platform options, filters, debounce, pagination, stored
details, status/priority updates, deletion confirmation/cancellation/retry, and
cache refresh after mutations. These results do not replace live browser checks.

The existing AddLibrarySchema migration was previously applied and tested against
clean databases. M3.6 creates no new schema or migration.

## Live acceptance environment

The API and frontend are running at https://localhost:7034 and
https://localhost:5173. PostgreSQL uses the existing persistent local volume.
Before live acceptance, the development database contained zero Games and zero
UserGames. Automated test data did not populate the development library.

Codex browser navigation stopped at ERR_CERT_AUTHORITY_INVALID for the Vite
development certificate. No browser security warning was bypassed. The project
owner handled local browser access and reported the browser flows below.
Codex did not independently complete those browser flows.

## Verified live additions

The project owner added Elden Ring using two different accounts: Backlog / High
for one account and Playing for the other. A read-only query against the local
development database independently confirmed:

- IGDB ID 119133 resolves to one shared Game named Elden Ring.
- There are exactly two UserGames belonging to two distinct owners.
- The Backlog record has High priority, BacklogOrder 1, and no StartedAt.
- The Playing record has null priority/order and a populated StartedAt.

This verifies real shared-snapshot reuse and separate persisted personal states.

## Owner-confirmed browser acceptance

The owner confirmed:

1. Opening one account's library-item URL while signed in as the other account
   produced the missing-item state.
2. Changing priority, reloading, and transitioning from Backlog to Playing did
   not affect the other account.
3. Library filtering and sorting worked correctly.
4. The supplied deletion flow worked: cancellation retained the item, confirmed
   deletion refreshed the list/discovery membership, and the other account was
   unaffected. The owner clarified that this check used another game, not Elden
   Ring.

A final read-only query still found one Elden Ring Game and two UserGames owned
by different users. Both personal records were Backlog / Low at that later point;
the owner was free to change their own statuses during acceptance. This query
does not independently prove the deletion of the unnamed other game.
The same query found the shared The Witcher 3: Wild Hunt snapshot (IGDB ID 1942)
with zero UserGames, demonstrating that an unowned shared snapshot remains in the
development database. No residual isolated test databases remained.

Foreign PATCH/DELETE responses, preservation of shared metadata after deletion,
all list filters, and pagination were verified by automated tests. No live foreign
mutation or manual next/previous-page test is claimed. Manual acceptance covers
the specification's two-user sharing, isolation, list controls, and deletion flow;
the additional pagination behavior is covered by the passing automated suite.

Rating/note editing belongs to M5 and backlog reordering belongs to M4; neither is
part of this acceptance task. Visual polish remains deferred to M7.

M3 is accepted for commit and push on feature/m3-my-library. No merge, pull
request, or M4 work is included. The local API/frontend servers remain running.
