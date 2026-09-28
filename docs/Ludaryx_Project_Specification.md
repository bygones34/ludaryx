# Ludaryx Project Specification

Version: 1.0 — implementation baseline  
Date: 2026-09-28  
Product: Ludaryx — **Your games. Your journey.**  
Audience: Product owner, ChatGPT Project, Codex, and contributors

## Document authority and provenance

This document consolidates the decisions available in the **Tech stack önerisi** conversation into the detailed source of truth for Ludaryx v1.0. README remains a separate, concise entry point covering setup, development, testing, and links to documentation. The intended repository location of this document is `docs/Ludaryx_Project_Specification.md`.

The accessible conversation contains the final API contract, architecture, roadmap, and specification structure. Earlier standalone product requirements and domain-model messages were not returned by the conversation reader. Product and entity descriptions below are therefore consolidated from those later decisions; missing details are explicitly identified as open decisions, rather than represented as previously agreed requirements.

The final game-details route overrides the earlier draft: **`GET /api/v1/games/igdb/{igdbId}`**. Architecture changes and additions to MVP scope require an explicit product-owner decision and a corresponding update to this document. Implementation clarifications must be recorded when resolved.

## Contents

1. Project Overview
2. Product Vision
3. v1.0 MVP Scope
4. Out of Scope
5. Technology Stack
6. Domain Model
7. Domain Rules
8. Database Design
9. IGDB Integration Strategy
10. API Contract
11. Authentication & Security
12. Backend Architecture
13. Frontend Architecture
14. Testing Strategy
15. Development Principles
16. Development Roadmap
17. Definition of Done
18. Codex Development Guidelines
19. Deployment Architecture
20. Future Backlog
21. Open Implementation Decisions

## 1. Project Overview

Ludaryx is a personal game-library and backlog-management web application. Users discover games through IGDB, add them to a private library, track their playing status, order their backlog, and record ratings and personal notes. A dashboard summarizes their gaming journey.

v1.0 is an authenticated application with a React frontend, a single ASP.NET Core API, and PostgreSQL persistence. IGDB supplies game metadata; Ludaryx stores shared game snapshots and user-specific records separately.

## 2. Product Vision

Help players answer: What am I playing? What should I play next? What have I completed, and how did I feel about it?

The experience should emphasize personal organization, a persistent ordered backlog, and a useful record of progress. Keep the product approachable and the implementation pragmatic. Deliver a working end-to-end product before expanding its architecture or introducing additional services.

Core journey:

```text
Register → Login → Discover → Add to Library → Backlog
         → Playing → Complete → Rate / Take Notes → View Journey
```

## 3. v1.0 MVP Scope

- Registration, login, logout, refresh-token rotation, current-user lookup, and protected routes.
- Authenticated game search and game details through IGDB.
- Private library: add, list, inspect, update, and remove games.
- Status tracking: Backlog, Playing, Completed, OnHold, and Dropped.
- Library search, filtering, sorting, and pagination from the outset.
- Backlog priorities, persistent order, and drag-and-drop reordering.
- Nullable personal ratings from 1 to 10 and personal notes up to 1,000 characters.
- Added, updated, started, and completed timestamps.
- Dashboard counts, average rating, Currently Playing, and Up Next.
- Responsive desktop, tablet, and mobile layouts; system/light/dark themes.
- Loading, empty, error, confirmation, and notification states; basic accessibility.
- Automated critical-flow coverage, CI/CD, and production deployment.

## 4. Out of Scope

Public profiles and anonymous application access are outside v1.0; the conversation explicitly defers them. No public-user lookup endpoint is included.

The 15-endpoint contract does not include social features, public reviews, recommendations, game-store synchronization, account-profile editing, password recovery, email verification, or administrative interfaces. These are excluded from this implementation baseline; their exclusion is a scope boundary inferred from the available contract, not a claim that each was separately discussed.

Do not implement speculative features or architectural expansion during MVP work. In particular, no microservices, Kubernetes, event bus, Redis, RabbitMQ, or MongoDB initially. Full local containerization of frontend and API is deferred; M0 needs Docker PostgreSQL only.

## 5. Technology Stack

| Area | Decision |
|---|---|
| Backend runtime | .NET 10 |
| HTTP API | ASP.NET Core Web API with controllers |
| Architecture | Pragmatic Clean Architecture; feature-based organization |
| Use cases | Lightweight CQRS; handlers invoked through DI; no MediatR initially |
| Persistence | PostgreSQL, EF Core, Npgsql, committed migrations |
| User accounts | ASP.NET Core Identity |
| Authentication | JWT access token + rotating refresh token in HttpOnly cookie |
| Validation | FluentValidation |
| Mapping | Explicit mapping; no AutoMapper initially |
| External metadata | IGDB through backend typed HttpClient |
| Frontend | React, TypeScript, Vite |
| Routing | React Router |
| Server state | TanStack Query |
| Small global client state | React Context; no Redux initially |
| Styling / components | Tailwind CSS, shadcn/ui |
| Backend tests | xUnit; API + PostgreSQL integration tests |
| Frontend tests | Vitest, React Testing Library |
| End-to-end tests | Playwright for critical journeys |
| Local database | PostgreSQL in Docker Compose |
| Source / CI | GitHub monorepo; GitHub Actions |
| Production | Cloudflare Pages frontend; Render API; Neon PostgreSQL |
| Logging | ILogger<T>, structured logging |

No generic repository or duplicate unit-of-work wrapper over EF Core. dnd-kit is a candidate for drag and drop, not a confirmed dependency. Testcontainers, Mapster, Zustand, and MediatR are possible later evaluations, not authorization to introduce them.

## 6. Domain Model

### 6.1 User account

ASP.NET Core Identity owns account identity and credentials. The API exposes `id`, `username`, `email`, `displayName`, and `createdAt` for the current user. Identity implementation belongs in Infrastructure; Domain must not depend on Identity or ASP.NET types.

Username and email are unique. Username length is 3–30 characters, email must be valid, and password length is at least eight characters. The exact additional password policy and display-name initialization remain implementation decisions.

### 6.2 Shared metadata

| Entity | Required concepts from the contract |
|---|---|
| Game | Local ID, unique IGDB ID, name, slug, summary, cover URL, release date |
| Genre | Local identity, unique IGDB identity, name |
| Platform | Local identity, unique IGDB identity, name, abbreviation |
| GameGenre | Game–Genre association |
| GamePlatform | Game–Platform association |

Game metadata is shared among users. Genres and platforms are many-to-many associations with games. Search results need not be persisted; a local snapshot is stored when a game is first added to a library.

### 6.3 UserGame

UserGame represents one user's personal record for one shared Game.

| Field | Meaning |
|---|---|
| Id | Library-item ID, distinct from Game.Id and Game.IgdbId |
| UserId | Owner resolved from authenticated claims |
| GameId | Shared local game reference |
| Status | Backlog, Playing, Completed, OnHold, Dropped |
| Priority | Nullable backlog priority |
| BacklogOrder | Nullable persistent backlog position |
| Rating | Nullable integer, 1–10 |
| PersonalNote | Nullable private note, at most 1,000 characters |
| AddedAt | When the user added the game |
| StartedAt | Set when entering Playing |
| CompletedAt | Set when entering Completed |
| UpdatedAt | Last update timestamp |

API local IDs are represented as UUID strings. IGDB IDs are external numeric identifiers. Keep these identifier spaces explicit in routes, DTOs, and persistence.

BacklogPriority values are `Low`, `Medium`, and `High`. Backlog is a view of UserGame records with `Status = Backlog`, not a separate entity. Dashboard is an aggregate response, not a separate persisted entity.

### 6.4 Refresh-token persistence

Infrastructure needs persisted refresh-token state to support expiration, rotation, and revocation. Account linkage and token validity are required concepts. Exact storage columns, hashing, and reuse-response policy must be finalized in M1; they were not specified in the accessible discussion.

## 7. Domain Rules

1. A user may have at most one UserGame for a Game. Duplicate additions return `409 Conflict`.
2. Different users may reference the same shared Game. Game metadata must not be duplicated for those users.
3. All private reads and writes are scoped to the authenticated user. Client-supplied UserId is never authoritative.
4. Removing a library item deletes UserGame only; it does not delete shared Game metadata.
5. Status and priority must be valid enum values.
6. Priority and BacklogOrder apply only to Backlog records. Leaving Backlog clears both fields.
7. A new backlog item can be appended using the user's maximum BacklogOrder plus one.
8. Reordering validates ownership, Backlog status, duplicate item IDs, and duplicate order values, then executes transactionally.
9. Rating is null or an integer from 1 to 10, inclusive. Notes have a maximum length of 1,000 characters.
10. Entering Playing sets StartedAt; entering Completed sets CompletedAt. Repeated transitions and date-reset semantics are not established and must be clarified before implementation.
11. Dashboard counts and lists contain only the current user's records. Null ratings are excluded from the average. Up Next respects BacklogOrder.

Validation is separate from business conflicts: invalid rating is `400`; an already-owned game is `409`. Client validation improves usability but never substitutes for server validation and database constraints.

## 8. Database Design

Use a single PostgreSQL database and EF Core migrations. Local development uses Docker PostgreSQL; production uses Neon PostgreSQL.

Logical tables:

```text
Identity tables ──┬── UserGame ─── Game ──┬── GameGenre ─── Genre
                 │                      └── GamePlatform ─── Platform
                 └── Refresh-token records
```

Required integrity:

- Unique normalized username and email through Identity configuration and database enforcement.
- Unique Game.IgdbId; shared Genre and Platform IGDB identities must not duplicate.
- Unique `(UserId, GameId)` on UserGame.
- Foreign keys for UserGame ownership, Game reference, and metadata associations.
- Unique association pairs for GameGenre and GamePlatform.
- Rating bounds and note-length enforcement; consistent null handling.
- Consistent backlog-only fields and order integrity.
- Deleting UserGame must preserve Game and its shared metadata.

Index ownership queries and common library/backlog access paths based on real queries. Do not add speculative indexes. Define UTC timestamp handling and release-date mapping consistently when configuring EF Core.

Shared-game creation must handle concurrent additions without duplicate snapshots. Backlog ordering must remain valid during transactional updates, including any uniqueness constraints; verify this with real PostgreSQL integration tests.

Every schema change requires a named EF Core migration committed to source control. Verify migration application against a clean database. Production migrations are a deliberate deployment step. Exact column nullability, enum storage, index definitions, cascade behavior for account deletion, and concurrency policy remain to be finalized during the relevant milestones.

## 9. IGDB Integration Strategy

- Browser calls Ludaryx API; IGDB credentials remain server-side.
- Infrastructure implements a typed HttpClient and IGDB authentication/token handling.
- Application consumes an appropriate interface, such as `IIgdbClient`; provider DTOs remain in Infrastructure.
- Explicitly map IGDB responses into Ludaryx response models.
- Search validates a query of at least two characters and initially limits results to 20.
- Details include cover, summary, release date, genres, platforms, and current-user library membership.
- Persist a shared metadata snapshot when a game is first added to a library; reuse the existing shared record on later additions.
- Handle unavailable IGDB, missing games, absent metadata, and upstream errors without crashing the application or leaking credentials.
- Do not call live IGDB during unit tests. Mock the client for application tests and use controlled responses for mapping tests.
- Verify real IGDB manually in M2 and during production smoke tests.

No Redis is required. Metadata refresh cadence, provider-token cache mechanics, retries, timeouts, and precise upstream-error status mapping must be documented when implemented. No background synchronization service is assumed.

## 10. API Contract

### 10.1 Conventions and endpoint inventory

Base path: `/api/v1`. JSON property names use camelCase. Enum examples use string values such as `Backlog`, `Playing`, and `High`. Business endpoints require authenticated access in v1. Registration and login are public; refresh and logout operate on the refresh-cookie session.

| # | Method | Route | Purpose |
|---:|---|---|---|
| 1 | POST | /api/v1/auth/register | Register account |
| 2 | POST | /api/v1/auth/login | Login |
| 3 | POST | /api/v1/auth/refresh | Rotate refresh token |
| 4 | POST | /api/v1/auth/logout | Revoke refresh token and clear cookie |
| 5 | GET | /api/v1/users/me | Current user |
| 6 | GET | /api/v1/games/search | Search IGDB |
| 7 | GET | /api/v1/games/igdb/{igdbId} | External-ID game details |
| 8 | GET | /api/v1/library | Current user's paginated library |
| 9 | POST | /api/v1/library | Add game |
| 10 | GET | /api/v1/library/{id} | Owned library-item details |
| 11 | PATCH | /api/v1/library/{id} | Update owned library item |
| 12 | DELETE | /api/v1/library/{id} | Remove owned library item |
| 13 | GET | /api/v1/backlog | Ordered backlog |
| 14 | PUT | /api/v1/backlog/order | Reorder backlog |
| 15 | GET | /api/v1/dashboard | Dashboard summary |

`{id}` in library routes means UserGame.Id. `{igdbId}` means the external IGDB identifier, never local Game.Id. `GET /health` is an operational endpoint outside the 15 business endpoints and outside `/api/v1`.

### 10.2 Authentication and current user

Register request:

```json
{ "username": "alper", "email": "alper@example.com", "password": "example-password" }
```

Success: `201 Created`, returning `id`, `username`, and `email`. Enforce the account validation and uniqueness rules in Sections 6 and 8.

Login request contains `email` and `password`. Successful response:

```json
{
  "accessToken": "<jwt>",
  "expiresIn": 900,
  "user": { "id": "<uuid>", "username": "alper" }
}
```

Set the refresh token in a HttpOnly, Secure cookie. Never return it in JSON. Access-token lifetime is 15 minutes; refresh-token lifetime is seven days.

Refresh needs no request body; the browser sends the cookie. Return a new `accessToken` and `expiresIn: 900`, and rotate the cookie token. The old refresh token cannot be reused. Logout revokes the token and clears the cookie. Logout's exact success response is an open contract detail.

`GET /users/me` returns `id`, `username`, `email`, `displayName`, and `createdAt`.

### 10.3 Discovery

`GET /games/search?q=elden%20ring`: query length at least two; initial limit 20. Return `{ "items": [...] }`. Each item includes `igdbId`, `name`, `coverUrl`, `releaseDate`, and platform names.

`GET /games/igdb/{igdbId}` returns `igdbId`, `name`, `slug`, `summary`, `coverUrl`, `releaseDate`, genre objects (`igdbId`, `name`), and platform objects (`igdbId`, `name`, `abbreviation`). Include the current user's membership:

```json
{ "library": { "isAdded": true, "userGameId": "<uuid>", "status": "Playing" } }
```

Represent the not-added case consistently; its exact optional-field shape remains to be fixed in M2. No local-ID game-details endpoint is part of v1.

### 10.4 Library

Add request:

```json
{ "igdbId": 119133, "status": "Backlog", "priority": "High" }
```

Resolve or create the shared Game snapshot, then create the owned UserGame. Return `201 Created` with `id`, a `game` object (`id`, `igdbId`, `name`, `coverUrl`), `status`, `priority`, `backlogOrder`, `rating`, `personalNote`, and `addedAt`. Duplicate ownership returns `409`.

List supports `status`, `platform`, `rating`, `priority`, `sort`, `page`, `pageSize`, and `search`.

```http
GET /api/v1/library?status=Completed&sort=ratingDesc&page=1&pageSize=20
```

Response has `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. Items include ID, game summary, status, rating, priority, addedAt, and completedAt. Page-size bounds, full sort vocabulary, defaults, and platform-filter identifier type must be fixed in M3; the example does not establish all defaults.

Library detail returns shared game summary/details, genres/platforms, and all personal fields including StartedAt, CompletedAt, and UpdatedAt. Enforce ownership for detail, update, and delete.

PATCH accepts partial updates, for example:

```json
{ "status": "Completed", "rating": 10, "personalNote": "Excellent ending." }
```

Success: `200 OK` with the updated UserGame DTO. Rating alone can be patched. Support nullable rating and priority; note length and enums are validated. Omitted fields must remain unchanged; document explicit-null semantics in M3/M5. Status-driven rules are applied on the server. Do not add separate rating or notes endpoints. Direct editing of dates was not specified.

Delete succeeds with `204 No Content`, removing only UserGame.

### 10.5 Backlog

GET returns `{ "items": [...] }`, ordered by BacklogOrder and scoped to the current user. Each item contains `id`, `order`, `priority`, and a game summary (`igdbId`, `name`, `coverUrl`). The DTO's `order` maps to the domain's BacklogOrder.

Reorder request:

```json
{
  "items": [
    { "userGameId": "<uuid-3>", "order": 1 },
    { "userGameId": "<uuid-1>", "order": 2 },
    { "userGameId": "<uuid-2>", "order": 3 }
  ]
}
```

Validate ownership, Backlog status, and duplicate IDs/order values. Apply atomically in a transaction. PUT success uses `200`; the exact response body and full-list-versus-partial reorder semantics must be settled in M4.

### 10.6 Dashboard

Single response contains:

- `statistics`: `totalGames`, `playing`, `completed`, `backlog`, `onHold`, `dropped`, `averageRating`.
- `currentlyPlaying`: items with `userGameId`, `igdbId`, `name`, `coverUrl`, and `rating`.
- `upNext`: items with `userGameId`, `igdbId`, `name`, `coverUrl`, `priority`, and `order`.

Ignore null ratings in the average and respect backlog order for Up Next. Empty-average representation, precision, list limits, and Currently Playing sort require clarification in M6.

### 10.7 Errors and status codes

Use ASP.NET Core ProblemDetails consistently, with field-level `errors` for validation. Do not expose stack traces or secrets.

```json
{
  "title": "Validation failed.",
  "status": 400,
  "errors": { "rating": ["Rating must be between 1 and 10."] }
}
```

| Situation | HTTP status |
|---|---:|
| Successful GET / PATCH / PUT | 200 |
| Resource created | 201 |
| Successful DELETE | 204 |
| Validation failure | 400 |
| Authentication required | 401 |
| Forbidden | 403 |
| Resource not found | 404 |
| Duplicate resource | 409 |
| Rate limit exceeded | 429 |
| Unexpected failure | 500 |

Use consistent ownership-failure behavior without exposing another user's private data. Exact 403-versus-404 behavior for foreign library IDs remains an implementation decision.

## 11. Authentication & Security

Identity manages accounts and password hashing. JWT authenticates API access; refresh tokens maintain sessions through a HttpOnly cookie. Keep access tokens in memory wherever possible. Never put refresh tokens in localStorage or expose them to JavaScript.

Required flows: login issues both tokens, refresh rotates and invalidates the old token, logout revokes refresh capability and removes the cookie, and browser reload restores authentication through refresh. Protected routes must wait for session initialization and handle refresh failure cleanly.

Resolve the current user from authenticated claims, and enforce ownership on every private query and command. Logout does not inherently revoke an already-issued stateless JWT; document its remaining validity until expiry rather than claiming immediate access-token revocation.

Security implementation requirements include server validation, database constraints, reviewed rate limiting, exact allowed CORS origins, HTTPS, and safe errors. Finalize cookie SameSite, path, domain, credentials behavior, and protection for cookie-authenticated actions in M1/M9. The original contract deliberately leaves SameSite environment-dependent. Verify deployment origins and browser cookie behavior before release.

Never log passwords, access tokens, refresh tokens, or secrets. Do not commit database passwords, JWT signing secrets, or IGDB client secrets. Use .NET user-secrets locally and Render environment variables in production. Frontend configuration contains public values only; a Vite environment variable is not a secret vault.

## 12. Backend Architecture

### 12.1 Repository and projects

```text
Ludaryx/
├── README.md
├── docs/
│   ├── Ludaryx_Project_Specification.md
│   ├── architecture/
│   └── decisions/
├── backend/
│   ├── Ludaryx.sln
│   ├── src/
│   │   ├── Ludaryx.Api/
│   │   ├── Ludaryx.Application/
│   │   ├── Ludaryx.Domain/
│   │   └── Ludaryx.Infrastructure/
│   └── tests/
│       ├── Ludaryx.UnitTests/
│       └── Ludaryx.IntegrationTests/
├── frontend/
├── docker-compose.yml
├── .gitignore
├── .editorconfig
└── LICENSE
```

### 12.2 Responsibilities and dependencies

- **Domain:** pure C# entities, enums, and domain rules. No EF Core, PostgreSQL, ASP.NET, Identity, JWT, or IGDB dependencies.
- **Application:** use cases, commands/queries, handlers, validation, DTOs, and necessary interfaces. Depends on Domain.
- **Infrastructure:** EF Core/Npgsql, entity configurations, migrations, Identity, JWT and refresh-token services, IGDB client. Depends on Application and Domain.
- **API:** controllers, HTTP concerns, middleware, exception handling, configuration, and composition root. Calls Application handlers. Infrastructure registration is wired at the composition root.

Application must not depend on the concrete Infrastructure project. Use narrow persistence interfaces or an appropriate DbContext abstraction; do not rebuild EF Core through a generic repository.

### 12.3 Feature organization

```text
Application/
├── Auth/
├── Games/{SearchGames,GetGameDetails}/
├── Library/{AddGame,GetLibrary,GetLibraryItem,UpdateLibraryItem,RemoveGame}/
├── Backlog/{GetBacklog,ReorderBacklog}/
├── Dashboard/GetDashboard/
└── Common/{Interfaces,Models,Exceptions}/
```

A feature keeps its command/query, handler, validator, and response beside one another. Commands change state; queries read data. Inject handlers directly without MediatR initially.

Infrastructure folders: `Persistence/Configurations`, `Persistence/Migrations`, `Identity`, `Authentication`, and `Igdb/Models`. API controllers: Auth, Users, Games, Library, Backlog, Dashboard.

Keep controllers thin. Business decisions belong in Application/Domain. FluentValidation handles inputs; application/domain rules handle ownership, conflicts, and transitions. Map DTOs explicitly and use structured ILogger<T> logging.

## 13. Frontend Architecture

```text
frontend/src/
├── app/
├── components/
├── features/{auth,games,library,backlog,dashboard}/
├── hooks/
├── lib/apiClient.ts
├── routes/
├── types/
└── main.tsx
```

Features can contain `api`, `components`, `hooks`, `pages`, and `types`. Shared components live outside features only when actually reusable.

Routes include `/login`, `/register`, `/games`, `/games/{igdbId}`, `/library`, and `/backlog`, with a protected dashboard as the post-login landing page. The precise dashboard and library-item frontend URLs are implementation choices.

Use React Router for navigation and protected routes, TanStack Query for server state, React Context for small auth/global state, and Tailwind/shadcn/ui for presentation. No Redux initially.

Centralize HTTP communication in `lib/apiClient.ts` and feature clients. Handle access tokens, credentialed refresh, standardized errors, and retry after refresh there. Coordinate simultaneous refresh attempts so rotation does not break concurrent requests. Clear user-specific cached data on logout or account change.

Search uses debounce and loading/empty/error states. Library supports filtering, sorting, search, and pagination. Backlog persists drag-and-drop changes. Rating, notes, and status editing use the existing library PATCH endpoint. Dashboard is fetched in one request.

Implement responsive layouts at desktop/tablet/mobile sizes, system/light/dark themes, skeletons, toasts, confirmation dialogs, consistent spacing, basic accessibility, and keyboard navigation. Provide an accessible way to reorder backlog items alongside pointer interaction.

## 14. Testing Strategy

Test meaningful behavior and critical failure modes rather than pursuing coverage percentages for their own sake.

- **xUnit unit tests:** rating and note bounds, status rules, backlog-only fields, reorder validation, and dashboard calculations.
- **Integration tests:** real API, EF Core, and PostgreSQL for constraints, authentication, ownership, transactions, and persistence. Testcontainers can be evaluated later; real PostgreSQL is the important requirement.
- **Vitest / React Testing Library:** critical forms, query states, protected navigation, and user interactions.
- **Playwright:** focused critical user journeys, introduced where useful rather than applied indiscriminately from day one.
- **IGDB:** mock application dependency and controlled provider responses for automated tests; live verification is a separate manual acceptance activity.

Critical end-to-end flow: Register → Login → Search Elden Ring → Add to Backlog → Start Playing → Rate 10 → Complete. Also verify logout/login persistence, isolation between two users, and persisted backlog order.

CI builds and tests backend and frontend. Deterministic automated tests should not depend on external IGDB availability. Manual acceptance results must be recorded; an unperformed check is not a pass.

## 15. Development Principles

- Prefer simple, explicit implementations over speculative abstractions.
- Use the monorepo and feature-based structure consistently.
- Keep HTTP, business logic, persistence, and provider-specific concerns in their assigned layers.
- Include migrations for all database changes and suitable tests for new behavior.
- Keep secrets outside source control and frontend bundles.
- Use English for repository documentation, identifiers, code comments, commit messages, PR descriptions, and API documentation. Product-owner discussions may remain Turkish.
- Implement small, reviewable tasks within a milestone; do not make a single oversized milestone commit.
- A milestone closes only after its automated tests and manual acceptance pass.
- Avoid unrelated refactoring while implementing a task.

Suggested Git workflow: `feature/m0-project-foundation`, `feature/m1-authentication`, and corresponding milestone branches; logical commits; PR review; merge to main after acceptance. Commit examples: `feat(auth): implement registration endpoint` and `test(auth): add authentication integration tests`.

## 16. Development Roadmap

Complete milestones in order: **M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9**. Each inherits the global Definition of Done in Section 17.

### M0 — Project Foundation

**Scope:** Create the four .NET 10 projects and two test projects; correct references and DI; React/TypeScript/Vite with Tailwind, shadcn/ui, React Router, and TanStack Query; feature folders; Docker PostgreSQL; EF Core/Npgsql configuration and application DbContext infrastructure; .gitignore, .editorconfig, README, environment configuration, user-secrets, and `/health`.

M0 verifies connectivity against real local PostgreSQL without creating an empty migration or an artificial table. The first meaningful EF Core migration and migration creation/application verification are deferred to M1, when ASP.NET Core Identity introduces the first persistent schema.

**Automated tests:** Backend and frontend build; test suites run; database connectivity against real local PostgreSQL and `/health` smoke checks. These checks consolidate the foundation acceptance requirements, rather than prescribing unnecessary placeholder tests.

**Manual acceptance:** On a clean setup, clone repository, start PostgreSQL with `docker compose up -d`, run API, install frontend dependencies, and run the frontend. Confirm frontend loads, database connects, and `GET /health` returns 200.

**Definition of Done:** Both builds pass; database and health check work; tests are runnable; setup is reproducible; secrets remain outside the repository.

### M1 — Authentication

**Scope:** Identity and the first meaningful EF Core migration; register/login/logout/refresh/current user; JWT 15 minutes and refresh seven days; rotation and HttpOnly cookie; login/register pages; auth state, protected routes, API-client handling, and session restoration.

**Automated tests:** Verify migration creation and application to clean PostgreSQL; successful registration; duplicate email/username rejected; invalid password rejected; successful login and wrong-password rejection; protected request without token returns 401 and valid token returns 200; refresh succeeds; old token cannot be reused; logout invalidates refresh.

**Manual acceptance:** Register, login, reload browser and confirm session restoration, logout, then attempt protected navigation. Verify auth cannot be restored through the revoked refresh token.

**Definition of Done:** End-to-end auth works; tokens are managed safely; critical auth integration tests pass; cookie and persistence policies are documented.

### M2 — Game Discovery / IGDB

**Scope:** Typed IGDB client, provider authentication/token handling, explicit mapping, upstream-error handling; game search and final IGDB-ID detail route; search debounce, cards, detail page, loading/empty/error states.

**Automated tests:** Query validation; search/details mapping; unavailable provider; unknown game; authenticated discovery behavior. Mock IGDB for unit tests.

**Manual acceptance:** Search Cyberpunk, Elden Ring, The Witcher, and Hades using real IGDB. Inspect covers, platforms, genres, and release dates; verify an error does not break the page.

**Definition of Done:** Real search and details work; upstream failures are handled; no provider credentials reach frontend.

### M3 — My Library

**Scope:** Game/Genre/Platform/association/UserGame entities and constraints; migrations; five library endpoints; first-add metadata snapshot; Add to Library interaction; library cards, search, pagination, filtering, and sorting.

**Automated tests:** Add succeeds; duplicate add returns 409; different users share one Game; metadata does not duplicate; list own library; foreign item inaccessible; update status; delete UserGame while preserving Game; filters, sort, and pagination behave correctly.

**Manual acceptance:** Create two users, add Elden Ring to both, and confirm one shared Game and two UserGame records. Verify neither user can read or change the other's item. Exercise list controls and deletion.

**Definition of Done:** Personal library is usable; isolation and metadata sharing hold; list operations work; migrations and integration tests pass.

### M4 — Backlog Management

**Scope:** Ordered backlog GET and transactional reorder PUT; priority and order only in Backlog; append new items; clear priority/order when leaving; backlog page and drag-and-drop. Evaluate dnd-kit if appropriate.

**Automated tests:** Ordered retrieval; append at end; reorder persistence; reject duplicate order/IDs, non-backlog items, and another user's items; leaving clears fields; invalid requests do not partially persist changes.

**Manual acceptance:** Add ten games, set priorities, reorder, reload browser, and confirm saved order. Move a game out of Backlog and verify fields clear.

**Definition of Done:** Priorities and order persist; drag-and-drop works; order integrity and user isolation are verified.

### M5 — Ratings & Personal Notes

**Scope:** Rating, PersonalNote, StartedAt, and CompletedAt via existing library PATCH; rating selector and note editor; no new endpoints.

**Automated tests:** Accept ratings 1, 10, and null; reject 0 and 11; accept a 1,000-character note and reject longer notes; Playing sets StartedAt; Completed sets CompletedAt; partial updates preserve omitted fields.

**Manual acceptance:** Backlog → Playing → rating 9 → add note → Completed. Logout/login and confirm all records persist.

**Definition of Done:** Personal lifecycle, rating, notes, and dates work; transition semantics are documented and tested.

### M6 — Dashboard

**Scope:** Dashboard endpoint and post-login page; all five status counts, total games, average rating, Currently Playing, and Up Next.

**Automated tests:** Empty library, single game, multiple statuses, null ratings excluded, correct average, Up Next respects BacklogOrder, and data isolated per user.

**Manual acceptance:** Compare dashboard values to a known library dataset, including unrated games and reordered backlog. Verify empty states and post-mutation refresh.

**Definition of Done:** Dashboard correctly summarizes real current-user data in one response; list and average policies are documented.

### M7 — UI / UX & Responsive

**Scope:** Desktop/tablet/mobile polish; system/light/dark themes; skeletons, empty/error states, toasts, confirmation dialogs, navigation, consistent spacing, accessibility basics, and keyboard navigation.

**Automated tests:** Focused frontend checks for key state rendering, theme controls, protected navigation, confirmation behavior, and critical keyboard interactions. These are consolidated verification requirements; the roadmap's original emphasis here was manual acceptance.

**Manual acceptance:** Inspect core screens at 375px, 768px, and 1440px; exercise themes, keyboard navigation, forms, dialogs, and backlog controls.

**Definition of Done:** Desktop, tablet, mobile, and dark mode work; no major layout breaks; critical controls remain usable.

### M8 — Quality & Hardening

**Scope:** Stop adding features. Review global exception handling, ProblemDetails, rate limiting, CORS, validation, constraints, authorization, structured logging, and applicable security headers. Audit secrets and address critical defects.

**Automated tests:** Integration coverage of register/login/refresh, search with controlled provider responses, add/update/backlog/rating/delete; critical frontend tests; Playwright journey Register → Login → Search Elden Ring → Add to Backlog → Playing → Rate 10 → Complete. Verify isolation and refresh invalidation regressions.

**Manual acceptance:** Review authentication/authorization boundaries, inspect errors and logs, verify secret handling and configured origins, and rerun the core user journey. Record findings and fixes.

**Definition of Done:** Critical journey passes automatically; no known critical bugs; authorization and secrets audits complete; required checks pass.

### M9 — Production Deployment

**Scope:** Neon database and production migration; Render API; Cloudflare Pages frontend; HTTPS, production CORS, environment variables, health check, CI/CD, production logging, and smoke testing.

**Automated tests:** CI backend/frontend builds and tests; migration verification; deployment health smoke checks; focused production-safe smoke verification. Document which checks are manual and avoid destructive smoke data operations.

**Manual acceptance:** On actual production URLs, register, login, search, add, reorder backlog, rate, complete, logout, and login again. Verify persistence and browser cookie behavior on desktop and phone.

**Definition of Done:** Production accessible over HTTPS; data persists; CI is green; CD works; health and smoke tests pass; no release-blocking auth or configuration problems.

## 17. Definition of Done

A task is done when its authorized behavior is implemented within the agreed architecture, appropriate automated checks pass, affected manual acceptance is verified, relevant migrations and documentation are included, and material limitations are disclosed.

A milestone is done only when all its scope, automated tests, manual acceptance, and specific DoD are complete. Passing a build alone does not close it. Any deferred requirement must be explicitly accepted and recorded; Codex cannot silently mark it complete.

v1.0 is done when M0–M9 are accepted and the production journey is usable: authenticate, discover, manage library/backlog, track playing/completion, rate/take notes, and view the dashboard.

## 18. Codex Development Guidelines

1. Read this specification and relevant repository instructions before implementation. Use it as the architecture and product baseline.
2. Do not introduce new frameworks or architectural patterns without explicit approval and a specification update.
3. Do not introduce MediatR, AutoMapper, generic repositories, Redis, RabbitMQ, MongoDB, microservices, Kubernetes, Redux, or an event bus into the initial implementation.
4. Follow the existing feature-based architecture and inward dependency direction.
5. Keep controllers thin; business rules belong in Application/Domain.
6. Resolve the current user from authentication claims. Never trust a client-supplied UserId.
7. Every database change requires an EF Core migration.
8. Include appropriate automated tests for new behavior, especially authentication, ownership, constraints, transitions, and ordering.
9. Do not modify unrelated code while implementing a task.
10. Prefer explicit mapping and simple implementations over speculative abstractions.
11. Never store secrets in the repository, browser bundle, or logs.
12. Preserve the 15-endpoint contract and the final IGDB detail route. Use existing library PATCH for ratings and notes.
13. Break milestone work into reviewable tasks: entities → mappings/migration → use case → endpoint → frontend → acceptance.
14. State what changed, what was verified, and what remains unverified. Never report unexecuted tests or manual checks as passed.
15. Do not close a milestone until automated tests and manual acceptance pass.
16. Treat future candidates and open decisions as unresolved, not implied authorization to add technology or features.
17. If a requirement is ambiguous, identify the ambiguity and resolve it with a documented decision at the relevant milestone. Do not manufacture previously agreed domain rules.

Suggested M3 task sequence: create shared entities; configure EF Core/migration; implement add use case; expose POST library with integration coverage; complete remaining library behavior and UI; run M3 acceptance; PR review.

## 19. Deployment Architecture

```text
GitHub monorepo
    └── GitHub Actions
         ├── Backend restore / build / test ──► Render (.NET API)
         └── Frontend npm ci / build / test ──► Cloudflare Pages (React)
                                                  │ HTTPS
                                                  ▼
                                              Render API
                                              ├── Neon PostgreSQL
                                              └── IGDB
```

Local: PostgreSQL in Docker Compose; API and frontend run from IDE/terminal. Do not make full application containerization a foundation prerequisite.

Production: frontend is a static Vite build; API runs on Render; Neon holds persistent data; only the backend calls IGDB. GitHub Actions validates changes; successful main builds may trigger deployment. Deployment must coordinate migrations and compatible API/frontend releases.

Configure production secrets through environment variables, exact CORS origins, HTTPS, credentialed cookie behavior, and health/logging. Hosting domains are not chosen in the available discussion; verify SameSite behavior with the actual frontend/API domain topology.

Before release, document production setup, migration procedure, smoke verification, and practical rollback/recovery handling. Provider plans, pricing, operational limits, backup policy, and custom-domain choices remain open; this specification records the agreed target architecture, not a claim that deployment has already occurred.

## 20. Future Backlog

These items are deferred candidates, not commitments or permission to implement:

- Anonymous game discovery and public profiles, explicitly deferred in the API discussion.
- Full local containerization of API and frontend.
- Testcontainers for automated PostgreSQL test provisioning.
- MediatR only if handler orchestration warrants it.
- Mapster only if explicit mapping becomes materially burdensome.
- Zustand only if client state outgrows Context.
- A dedicated production logging platform when operational needs justify it.
- Metadata-refresh policies and other additions after v1.0 acceptance.

Other product ideas must be evaluated separately. Do not assume social features, store integrations, recommendation systems, or infrastructure expansion have been approved.

## 21. Open Implementation Decisions

The available source does not resolve the following details. Resolve and record them before implementing the affected behavior; preserve the established architecture while doing so.

| Milestone | Details to finalize |
|---|---|
| M0 | Exact package versions, local ports, configuration names, health-check depth, license |
| M1 | Additional password policy, display-name default, refresh-token schema/hash/reuse policy, cookie settings, logout response, CSRF/origin protection |
| M2 | Missing metadata representation, not-added library shape, upstream error mapping, provider token/cache/retry/timeout policies |
| M3 | Schema/nullability/enum storage, list defaults and bounds, complete sort vocabulary, platform filter type, PATCH null semantics, ownership error status, concurrency behavior |
| M4 | Default/nullable backlog priority, first order value, order bounds/compaction, full versus partial reorder, response body, concurrent reorder policy |
| M5 | Timestamp overwrite/reset rules for repeat transitions and reversals; whether dates are editable |
| M6 | Empty average, precision/rounding, Currently Playing order and list limits, Up Next limit |
| M9 | Domains, cookie topology, provider configuration, migration/recovery/backup procedures |

This list prevents missing details from being mistaken for historical agreement. Once resolved, update the relevant normative sections and keep this document synchronized with implementation.
