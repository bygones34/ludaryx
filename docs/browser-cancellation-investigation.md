# Browser cancellation investigation

Date: 2026-10-13

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
