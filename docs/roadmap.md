# Roadmap

FleetGo is built in phases. Each phase is a vertical slice - backend, mobile and tests together -
so the app is runnable and demonstrable at the end of every one.

## Phase 1 — Foundation ✅

Solution, projects and references; shared contracts; OpenAPI and health checks; MAUI app with DI,
Shell and a landing page that verifies connectivity; integration and unit test projects; CI.

## Phase 2 — Identity and access

- EF Core + SQL Server, first migration, driver and user tables
- Login endpoint issuing JWT access tokens and refresh tokens, with rotation and revocation
- Token storage on device using `SecureStorage` (Keychain / Android Keystore)
- Authenticated `HttpClient` handler that refreshes on 401 and retries once
- Login screen, session restore on launch, sign-out

## Phase 3 — OTP and biometrics

- OTP request/verify endpoints with rate limiting and expiry
- Android SMS Retriever autofill; iOS one-time-code keyboard support
- Biometric unlock for an existing session

## Phase 4 — The working day

- Vehicle, customer, route, stop and package APIs
- Pagination, filtering and sorting on list endpoints
- Driver dashboard, route list, stop detail
- Search and filter on device

## Phase 5 — Offline first

- SQLite local store and a local-first read path
- Outbox for mutations made offline, with conflict handling
- Background synchronisation and retry with exponential backoff
- Connectivity detection and honest UI state

## Phase 6 — Proof of delivery

- Camera capture, digital signature, document upload/download
- Barcode and QR scanning for packages
- POD submission endpoint with attachments

## Phase 7 — Location

- Foreground and background location tracking, with the platform permission flows
- Batched location upload endpoint
- Map and route visualisation

## Phase 8 — Notifications and deep links

- Device registration endpoint and push delivery
- Deep links from a notification into the right stop

## Phase 9 — Payments

- Sandbox payment gateway integration for cash-on-delivery and card capture
- Payment records and reconciliation endpoints

## Phase 10 — Production readiness

- Structured logging and diagnostics, correlation IDs
- Resilience policies, timeouts and circuit breakers
- App version and forced-update handling
- Release pipelines for the mobile heads

*Phases after 2 may be reordered as the app takes shape.*
