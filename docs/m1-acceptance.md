# M1 authentication acceptance record

Date: 2026-10-03

## Automated checks

| Check | Result |
|---|---|
| Backend build | Passed; 0 errors and 0 warnings |
| Backend tests | Passed; 26 tests, including real PostgreSQL authentication and refresh flows |
| Clean PostgreSQL migration application | Passed; Identity and refresh-session migrations applied to a newly created temporary database |
| EF model consistency | Passed; no pending model changes |
| Frontend build and lint | Passed |
| Frontend tests | Passed; 5 tests covering anonymous routing, registration/login/logout, failed login, restoration, and shared refresh |
| Frontend dependency audit | Passed; 0 reported vulnerabilities |
| Git whitespace check | Passed |

The clean migration test creates a uniquely named database on the existing
local PostgreSQL server, applies the migrations, verifies the schema is
queryable, and removes only that database. No temporary test database remained
after the run. The development database and the user's manually registered
account were preserved.

Backend coverage includes duplicate account rejection, password validation,
login, bearer validation, current-user authorization, refresh cookie security,
single-use rotation, session-family revocation after reuse, isolation between
sessions, concurrent refresh attempts, idempotent logout, Origin rejection,
and credentialed CORS.

## Manual browser acceptance

The project owner confirmed the following sequence using the local development
site and API:

1. Register an account.
2. Sign in successfully.
3. Reload the page and confirm that the session is restored.
4. Sign out.
5. Reload again and confirm that the sign-in screen remains visible.

This confirms the browser cookie and restoration flow on the tested local
setup. Automated tests separately verify that logout revokes refresh
capability and anonymous navigation cannot show the protected home page.
The API health check also returned HTTP 200 with `Healthy`.

## Token and environment policies

Access tokens expire after 15 minutes and stay in frontend memory. Refresh
tokens last seven days, are stored only as SHA-256 hashes in PostgreSQL, and
are sent through a host-only, HttpOnly, Secure, SameSite=Lax cookie scoped to
`/api/v1/auth`. Rotation and revocation are serialized within each session
family. Logout clears frontend auth state and cached user data; an already
issued stateless access token remains valid until expiry.

Local development uses HTTPS localhost for both frontend and API, exact
credentialed CORS origins, and Origin validation for refresh/logout. Database
credentials and the JWT signing key remain outside tracked configuration.
Setup is documented in `local-development.md`.

The local browser acceptance was reported by the project owner. Codex's own
browser attempt stopped at the untrusted Vite development-certificate error.
Production domains, cross-site cookies, and production certificate behavior
remain M9 deployment work. No production deployment was tested.

API and frontend development servers were stopped after manual acceptance.
M1 implementation and acceptance are complete on `feature/m1-authentication`.
