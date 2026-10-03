# M0 foundation acceptance record

Date: 2026-10-03

## Automated checks

| Check | Result |
|---|---|
| Backend restore and build | Passed; final standalone build had 0 errors and 0 warnings |
| Backend tests | Passed; 5 tests, including real PostgreSQL connectivity and health responses |
| Frontend `npm ci`, build, lint, and test | Passed; 1 frontend test |
| Frontend dependency audit | Passed; 0 reported vulnerabilities |
| Git whitespace check | Passed |

## Local acceptance

A temporary local clone of `feature/m0-project-foundation` was used for the
clean-checkout exercise. It contained the committed M0.1–M0.8 changes. The
ignored `.env` file was copied from the existing workspace, and the API used
the existing machine-level .NET user-secret. Docker PostgreSQL used the
existing named volume; this was not a test on a newly provisioned machine or
an empty database volume.

From the clone, `docker compose up -d --wait` reached a healthy PostgreSQL
state. Backend restore, build, and all 5 tests passed. `npm ci`, frontend
build, lint, and its test passed. The API and Vite servers both started.
`GET /health` returned HTTP 200 with `Healthy`, and the frontend HTML and
foundation-screen module returned HTTP 200. The frontend DOM smoke test
confirmed the visible heading and disabled button.

The API's HTTP-only development profile logged an HTTPS-redirection-port
warning during the `/health` request; the response remained HTTP 200.
This warning does not affect the M0 health check, but local HTTPS behavior
should be reviewed before production deployment.

M0 has no EF Core migrations or application tables by decision. Migration
creation and application will be verified in M1 when Identity adds the first
persistent schema. Browser visual inspection and setup on a separate machine
have not been performed.
