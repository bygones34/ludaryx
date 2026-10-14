# M4.3 backlog frontend verification

The protected `/backlog` page implements ordering and priority changes using the
M4.2 API. The approved `@dnd-kit/react` dependency supplies pointer and keyboard
sorting. Exact dependency versions are recorded in package.json and its lockfile.

## Automated checks

- All 53 frontend tests pass, including 15 backlog flow tests.
- Coverage includes authentication, empty/loading/error states, full-list reorder
  payloads, server-authoritative responses, pending-write controls, rollback and
  retry, conflict reload, failed reload blocking, priority-only updates, cache
  invalidation, navigation, and late responses after an account change.
- The production TypeScript/Vite build and ESLint pass.

## Browser checks

An ignored temporary browser fixture rendered the real application and backlog
components with three synthetic games and mocked API responses. It did not write
to the development database or modify the signed-in account's library.

- Pointer drag moved the first game to the last position and sent all three IDs
  with contiguous positions.
- Space, ArrowDown, and Space reordered a game; focus returned to its handle
  after saving.
- Escape canceled a keyboard drag without a new reorder request.
- Move down buttons reordered games successfully.
- Priority changes and saved order survived fixture reloads.
- No browser console errors were observed during these fixture checks.

These checks verify browser interaction, not end-to-end database persistence.
The real PostgreSQL backend checks belong to M4.2. Full milestone acceptance with
real accounts and at least ten games remains pending; M4.3 does not mark M4 complete.
