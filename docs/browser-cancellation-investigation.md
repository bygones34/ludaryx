# Browser cancellation investigation

Date: 2026-10-13

Last investigation update: 2026-10-18

Status: The AbortError warning is reproducible in the owner's Edge environment
without Ludaryx, React, or TanStack Query code. Its underlying browser cause is
not yet established. No application-level cancellation workaround is applied.

## Confirmed local development fixes

Vite's previous basic-ssl certificate failed normal certificate validation.
A client explicitly trusting that certificate could complete the HMR handshake,
showing that the WebSocket server itself was available. Vite now uses the already
trusted ASP.NET Core development certificate, exported into the ignored
frontend/.certs directory. No trust-store changes or disabled TLS validation were
needed. Standard HTTPS returned 200 and a standard ClientWebSocket reached Open.
The owner subsequently no longer reported the WebSocket errors.

The HTML now explicitly specifies an empty favicon. There is no designed brand
icon yet; this prevents the browser's implicit /favicon.ico request. The owner's
latest screenshot contained no favicon 404.

## Application checks

The original warning occurs when an inactive query's AbortSignal is cancelled.
The owner reproduced it on entering My Library in Edge, including InPrivate.
Temporary diagnostics correlated the reason with /library but could not identify
the rejected promise as the fetch, response JSON, API result, or its tracked
continuations. The browser's fetch and Response.json appeared native.

Focused tests cover cancellation before a response, during body reading, on
search changes, on unmount, and during StrictMode's library remount. All 38
frontend tests pass; build, lint, and git diff --check pass. Mocked responses in
these tests do not reproduce native Edge behavior.

Codex browser navigation through Home, Discover, and My Library, repeated rapid
Home/library transitions, status-filter changes, and reload produced no error.
That account's library was empty; this alone was not a controlled comparison of
the owner's exact application dataset.

## Independent native-fetch reproduction

The credential-free reproduction is in diagnostics/fetch-cancellation.html.
It uses native fetch and AbortController without application imports, React, or
TanStack. Each case repeats five times and catches AbortError explicitly. The
API case uses an intentionally invalid diagnostic bearer value, never a real
token. No business data is written or authenticated data read.

| Case | Caught aborts in each browser | Codex unhandled rejections | Owner's Edge unhandled rejections |
|---|---:|---:|---:|
| Same-origin JSON request | 5 | 0 | 5 |
| Same-origin JSON body | 5 | 0 | 5 |
| Cross-origin API request | 5 | 0 | 5 |
| Cross-origin API body | 5 | 0 | 0 |

Both user-agent strings reported Chrome/155.0.0.0 on Windows; Edge additionally
reported Edg/155.0.0.0. These reduced user-agent strings do not establish exact
installed browser builds. Codex observed its result directly; the owner supplied
the Edge result. The page is served by Vite, so this does not rule out all browser
environment or development-tool interactions. It does prove that the warning
does not require the library implementation.

To reproduce with the development servers running, copy the static probe into
the ignored test-output directory from the repository root:

```powershell
New-Item -ItemType Directory -Force frontend/TestResults | Out-Null
Copy-Item docs/diagnostics/fetch-cancellation.html frontend/TestResults/cancellation.html
```

Open https://localhost:5173/TestResults/cancellation.html and select Run
cancellation checks. Wait for Complete. The copied page is a local diagnostic
artifact and is not an entry point in the production build.

## Outcome and limits

Keep normal request cancellation and error handling. No global AbortError
suppression, disabled StrictMode, or package upgrade was introduced to hide the
warning. The temporary Promise-prototype tracing and unhandled-rejection logger
have been removed; apiClient.ts is unchanged from the committed version.

The Edge-environment warning remains unresolved. A browser update/profile/build
comparison or a browser-vendor report with the standalone reproduction is a
reasonable next investigation; no vendor-confirmed defect or fixed version was
found. Microsoft's [Edge issue-reporting guidance](https://microsoftedge.github.io/MSEdgeExplainers/)
identifies Send Feedback as the reporting channel. No external report was sent.

API, frontend, and PostgreSQL remain running for local testing. No commit/push
or M4 implementation was performed during this investigation.


## M7.6 automated re-investigation — 2026-10-18

The owner requested preparatory regression and investigation now, stopping before
final owner live-browser acceptance. README and acceptance records are reserved
for that later step. This section records technical investigation only.

### Current environment and controlled application

The available automation surfaces were the in-app Chromium browser and MCP Apps;
no Edge browser connection or native app-control surface was available. Reading
only the installed Edge executable metadata reported **155.0.4283.62**. That
identifies the installed executable, not the exact build/profile from the prior
owner reproduction. The automated browser still reported the reduced user agent
Chrome/155.0.0.0 without Edg; it must not be presented as an Edge test.

Ran the actual frontend entry point against the real HTTPS API/application/EF
stack, backed by a newly generated, migrated PostgreSQL test database. Only IGDB
was replaced with deterministic synthetic metadata and controlled delay/failure
responses. Authentication, HTTP, CORS, secure refresh cookies, library/backlog
writes, dashboard projections, reload and re-login persistence were real. The
owner's library and external IGDB service were not used for this journey.

For cancellation tracing, a temporary alternate HTML entry imported the unchanged
frontend entry point. It counted window error/unhandledrejection events without
preventDefault or suppression. A diagnostic fetch wrapper observed AbortSignal
abort events and returned the original native fetch promise directly; it added
no promise continuations and did not modify Promise/Response prototypes.
Production frontend source and apiClient.ts were not changed.

After 20 rapid Dashboard/Library transitions, confirmed-start slow discovery
requests, leaving a pending search, and changing search text, the final counters
were **52 requests, 43 observed abort signals, zero unhandled rejections, zero
unhandled AbortErrors, and zero window errors**. Abort-signal counts include
signals observed by the wrapper, not a claim that every one aborted an unfinished
network operation. Slow-request start counters confirmed the pending-request
checks had reached fetch before cancellation.

### Native fetch comparison with development tooling removed

Repeated the existing credential-free probe in two configurations, using the
same HTTPS localhost origin and existing trusted development certificate:

1. Served through Vite, without React or TanStack imports in the probe.
2. Served by a minimal Node HTTPS static server, with Vite stopped. No external
   scripts, HMR client, React, TanStack, or application code were loaded. The one
   inline module was the native-fetch probe itself. The same-origin payload was
   the same package.json; cross-origin requests reached the real isolated API
   with the deliberately invalid diagnostic bearer and no account credentials.

| Native fetch case | Caught aborts / other errors / unhandled, Vite | Caught aborts / other errors / unhandled, static HTTPS |
|---|---|---|
| Same-origin JSON request | 5 / 0 / 0 | 5 / 0 / 0 |
| Same-origin JSON body | 5 / 0 / 0 | 5 / 0 / 0 |
| Cross-origin API request | 5 / 0 / 0 | 5 / 0 / 0 |
| Cross-origin API body | 5 / 0 / 0 | 5 / 0 / 0 |

### Outcome, evidence, and cleanup

The warning was **not reproduced in the available Chromium automation**, either
in the real application or in native fetch with/without Vite. This does not
establish the underlying cause of the historical Edge-only observation, prove a
vendor fix, or eliminate an Edge/profile/development-tool interaction. The exact
Edge comparison remains unverified and the original issue stays **unresolved**.
No functional cancellation blocker was reproduced in this run. Keep normal
cancellation and error handling; no error suppression, disabled StrictMode,
certificate bypass, dependency upgrade, or speculative application workaround
was introduced. No vendor report was sent.

Saved machine-readable counters, probe output and screenshots in the task's
visualization output directory (m7-6-application-cancellation.json,
m7-6-native-probes.json, and m7-6-native-cancellation.png). The ignored technical
run report is backend/TestResults/m7-6-verification.md. The permanent API journey
regression is backend/tests/Ludaryx.IntegrationTests/ApplicationJourneyTests.cs.
The final full suite passed 305 backend tests and 141 frontend tests, both builds,
and frontend lint. These are preparatory checks, not final M7 acceptance.

The controlled API shut down and its generated test database was removed.
Temporary tracing/static-server and browser-host source was removed after use;
frontend/API listeners were confirmed stopped and PostgreSQL was stopped with
its persistent development volume preserved. Agent tabs were closed, the
viewport reset, and System theme restored. README/acceptance documentation was
not updated, and no commit, push, or owner live acceptance was performed.
