# Architecture

This document records *why* FleetGo is put together the way it is. Each entry is a decision
someone could reasonably have made differently, with the trade-off that settled it.

## Shape of the solution

```
FleetGo.Mobile  ──┐
                  ├──►  FleetGo.Shared  ◄──  FleetGo.API
FleetGo.Mobile.Tests ┘                        ▲
                                              │
                                    FleetGo.API.Tests
```

Three source projects. `FleetGo.Shared` is referenced by both ends and references nothing itself.

### Why a shared project at all

The alternative is to declare the response DTOs twice - once in the API, once in the app - and keep
them in step by hand. That works right up until someone renames a property on the server and the
mobile app silently starts showing empty fields, because JSON deserialisation does not fail, it just
produces nulls.

`FleetGo.Shared` removes that class of bug. It owns:

- **Contracts** (`ApiInfoResponse`, `HealthReportResponse`, and - since Phase 2 - the `Auth`
  contracts) - the payload shapes.
- **`ApiRoutes`** - the URLs, as constants. The API maps them; the client calls them; the tests
  assert against them. A typo cannot affect only one side.
- **JSON settings** - one `JsonSerializerOptions` and one source-generated serializer context, so
  the bytes the server writes are read by the client with identical rules.
- **`FleetGoApiClient`** - a typed client over `HttpClient` that speaks those contracts.

It deliberately has **no NuGet dependencies** and no reference to ASP.NET Core, EF Core or MAUI.
That constraint is what makes it safe for a mobile app to consume, and it is the line that keeps
"shared" from quietly becoming "everything". It is also why the HTTP-layer abstractions Phase 2
needed - `IAccessTokenProvider`, `BearerTokenHandler` - live here rather than in the mobile app:
they are plain interfaces and a `DelegatingHandler` with zero platform dependency, so the typed
client stays capable of authenticated calls without knowing anything about `SecureStorage`,
Keychain, or how a token is actually kept.

### Why the API client lives in Shared rather than in the mobile app

It is a plain `HttpClient` wrapper with no platform dependency, so it costs nothing to host it next
to the contracts it speaks - and doing so means it can be unit tested from an ordinary `net10.0`
test project, without an emulator. Configuration (base address, timeout, retry policy) is *not* in
the client: it is applied by whoever registers it, which in the app is `MauiProgram`.

## Backend

### Minimal APIs over MVC controllers

The endpoints in this application are thin: validate input, call a service, return a result. Minimal
APIs express that with less ceremony, start faster, and route registration groups naturally by
feature (`MapSystemEndpoints`, `MapAuthEndpoints`, later `MapRouteEndpoints`). Controllers earn
their keep when you need filters, model binders and conventions layered on top - if a later phase
needs that, a controller can be added alongside; the two coexist in one host.

### Source-generated JSON on both sides

The server registers the same `FleetGoJsonSerializerContext` the mobile app uses. On the server it
is a small startup and throughput win. On mobile it matters more: release builds are trimmed and iOS
builds are AOT compiled, and reflection-based serialisation is exactly what trimming removes.
Generating the serialisation code at compile time sidesteps the problem instead of patching it with
trimmer hints later. It is also why `FleetGo.Mobile.Core`'s `StoredTokens` (Phase 2's on-device
token record) is deliberately a *different* type from the wire contract `TokenResponse`, stored as
four separate `SecureStorage` string entries rather than one serialised blob: it never needs to be
part of that generated context, because it never crosses the wire.

### Health checks that return a real contract

The stock `/health` endpoint writes the plain string `Healthy`. FleetGo projects the health report
onto `HealthReportResponse` so a client can display *what* is unhealthy. Three endpoints are
exposed, distinguished by tag:

| Endpoint | Question it answers |
|----------|--------------------|
| `/health` | Everything - used by humans and dashboards |
| `/health/live` | Is the process up? (restart me if not) |
| `/health/ready` | Can it serve traffic? (route to me if so) |

Splitting liveness from readiness matters the moment the API depends on SQL Server: a database
outage should stop traffic being routed to the instance, but restarting the container will not fix
it. Phase 2 is that moment - `AddDbContextCheck<FleetGoDbContext>` is tagged `"ready"` only, so a
database outage never makes an orchestrator restart an otherwise-healthy process.

### Errors as `problem+json`

`AddProblemDetails()` with `UseExceptionHandler()` and `UseStatusCodePages()` means every failure -
unhandled exception, 404, 405 - reaches the client as one predictable JSON shape (RFC 9457) instead
of an HTML error page the mobile app cannot parse. `AuthEndpoints` reuses the same mechanism for
expected auth failures (`TypedResults.Problem(..., statusCode: 401, ...)`) rather than inventing a
separate error shape for one feature area.

### API versioning

Version 1 is expressed in the path (`/api/v1/...`) via the shared `ApiRoutes.Base` constant. A
formal versioning library (`Asp.Versioning`) is worth adding when a v2 actually exists and two
versions must be served side by side; adding it now would be configuration without a consumer.

## Phase 2: authentication

### Password hashing: `PasswordHasher<TUser>`, not the full ASP.NET Core Identity system

`Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` (package `Microsoft.Extensions.Identity.Core`)
is a stand-alone, battle-tested PBKDF2 implementation with automatic rehash-on-verify when its
default work factor moves on (`AuthService` re-hashes on `SuccessRehashNeeded`). Pulling in the
*full* Identity system - `UserManager<TUser>`, `IdentityUser`, EF Core stores, roles, claims
transformation - would mean adopting its schema and conventions for a `User`/`Driver` model this
project already owns and wants to keep simple. This is the same trade-off `Directory.Build.props`
already made for "no repository layer, no Application/Domain/Infrastructure split": use the
narrow, well-established piece that solves the actual problem, not the framework built to solve
every problem like it.

### JWT access tokens + rotating refresh tokens, not server-side sessions

A stateless, short-lived (15 minute default) JWT means most requests need no database round trip to
authenticate - the signature alone proves the claims. The trade-off with any stateless token is that
it cannot be revoked before it expires; refresh tokens are how FleetGo bounds that risk without
paying for a server-side session store on every request:

- The refresh token is a 512-bit random value, **never** the thing validated cryptographically like
  a JWT - `RefreshToken.TokenHash` stores its SHA-256 hash, not the token itself, so a leaked
  database backup does not hand out working tokens. A slow, salted password hash (PBKDF2/bcrypt)
  would protect against nothing extra here (the input is already high-entropy random, not a
  human-chosen password) and would make every refresh call measurably slower for no benefit.
- **Rotation**: every refresh call revokes the presented token and issues a new one
  (`AuthService.IssueTokenPairAsync` with `rotatedFrom:`). A client that only ever holds the latest
  token is unaffected; a copy of an old token stops working the moment the legitimate client
  refreshes past it.
- **Reuse detection**: a refresh call presenting a token that is already revoked - not merely
  expired - is treated as a signal the token was copied, and revokes every other active refresh
  token on that account (`AuthService.RefreshAsync`). The legitimate client is logged out too, which
  is the trade-off: without server-side sessions there is no way to invalidate *only* the attacker's
  copy, so the safer failure is to force a fresh login everywhere.
- Login and refresh return the **same** `TokenResponse` shape - one contract, not `LoginResponse`
  and a duplicate `RefreshTokenResponse` - because rotation makes them the same operation from the
  client's point of view: "here is a valid token pair now."

### Login, wrong password, and an inactive account fail identically

`AuthService.LoginAsync` returns the same `AuthOutcome.Failed` - same HTTP status, same generic
problem-detail message - whether the email does not exist, the password is wrong, or the account is
inactive. Distinguishing them in the response is exactly what would let a caller enumerate which
email addresses have accounts, or learn that a specific account has been deactivated. What is *not*
mitigated: response-time differences between the three cases (an unknown email skips password
verification entirely; a known one does not), which a sufficiently patient attacker could measure.
Closing that gap - a dummy hash comparison on the unknown-email path, or better, a login-attempt
rate limiter - is a reasonable Phase 3+ addition once real traffic makes it worth the complexity.

### `JwtBearerOptions` configured from `IOptions<JwtOptions>`, not read from `IConfiguration` directly

`Program.cs` binds the signing key/issuer/audience through the options pipeline
(`AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtOptions>>(...)`) instead of the more
obvious-looking `builder.Configuration.GetSection("Jwt").Get<JwtOptions>()` read directly into an
`AddJwtBearer(options => ...)` lambda. The latter evaluates at that exact line in `Program.cs` -
before `builder.Build()` - which freezes in whatever configuration exists at that point. That is a
problem specifically for `FleetGoApiFactory` (the integration tests): `WebApplicationFactory`
layers its test configuration (a fixed test signing key, so tests never depend on a developer's
`user-secrets`) onto the builder as part of building the host, and code that already ran before
`Build()` never sees it. Resolving through `IOptions<T>` defers evaluation until DI actually
constructs `JwtBearerOptions` - after the host, and the test factory's overrides, are fully
assembled.

### `Jwt:SigningKey` fails fast; a bad `ConnectionStrings:FleetGoDatabase` does not

Both are required configuration, but they fail differently on purpose. `JwtOptions` is validated
with `.ValidateOnStart()`: a missing or too-short signing key is a pure configuration mistake with
one right answer, so the API refuses to start at all rather than mint tokens with a weak key. A
database that cannot be reached is an *operational* condition, not a configuration mistake - the
process itself is still healthy, requests just cannot currently reach SQL Server - which is exactly
the liveness-vs-readiness distinction the health checks above exist to report. Crash-looping the
whole process over a transient database outage would be worse than reporting `/health/ready` as
unhealthy and letting the orchestrator stop routing traffic.

### Test database: SQLite in-memory, not the EF Core InMemory provider

`FleetGoApiFactory` swaps SQL Server for an open, in-memory SQLite connection (kept alive for the
factory's lifetime - an in-memory SQLite database exists only as long as a connection to it is
open). The EF Core InMemory provider was deliberately avoided: it does not enforce unique indexes,
foreign keys, or general relational constraints, which is precisely the behaviour
`AuthEndpointsTests` needs to trust (a duplicate email, a token hash collision). SQLite is a real
relational engine and catches those the same way SQL Server would; the SQL dialect differences that
remain (its Fluent API configuration in `Data/Configurations/` uses no SQL-Server-specific column
types) do not affect this schema.

## Mobile

### MVVM with CommunityToolkit.Mvvm

Views bind to view models; view models depend on abstractions (`IFleetGoApiClient`, `IConnectivity`,
`TimeProvider`) and never on pages. `[ObservableProperty]` and `[RelayCommand]` generate the
boilerplate at compile time, so the interesting code in a view model is the part that is actually
worth reviewing.

### Compiled bindings are on

`MauiEnableXamlCBindingWithSourceCompilation` plus `x:DataType` on every binding scope turns bindings
into generated code: they are faster, they survive trimming, and a renamed property becomes a build
error rather than a blank label discovered by a tester.

### DI as the composition root

`MauiProgram` is the only place that knows how anything is constructed. `IHttpClientFactory` supplies
the `HttpClient` (pooled handlers - a long-lived static client misses DNS changes and a per-call
client exhausts sockets), and it is the seam where retry, timeout and circuit-breaker policies plug
in during the resilience phase - and, since Phase 2, where `BearerTokenHandler` is attached
(`.AddHttpMessageHandler<BearerTokenHandler>()`) so every call through `IFleetGoApiClient` reaches
an authenticated endpoint without each call site remembering to attach a token itself.

### Platform reach-back to the API

Each platform has a different idea of "the machine running the API": the Android emulator uses
`10.0.2.2`, the iOS simulator and Mac Catalyst use `localhost`. `ApiSettings` resolves this once.
Android's cleartext-HTTP block is opened for those two hosts only, via a network security config -
not by switching cleartext on globally.

### `FleetGo.Mobile.Core`: resolving the "known gap" below

Phase 1 flagged (see the retained note below) that a `net10.0` test project cannot reference the
`FleetGo.Mobile` app project, because it only targets platform heads, and named "extract a plain
`net10.0` library for view models and services, referenced by both the app and the tests" as the
plan for when a view model's logic justified it. `LoginViewModel` - real validation, a real
authentication flow, real failure-message mapping - is that point, so `FleetGo.Mobile.Core` now
holds it, `AuthenticationService`, `ISecureTokenStore`, and their supporting types.

It is deliberately **not** `UseMaui` (unlike the option originally sketched): no
`Microsoft.Maui.Controls` reference at all, only `CommunityToolkit.Mvvm` and `FleetGo.Shared`. A
platform capability such as `SecureStorage` is reached through an interface defined here
(`ISecureTokenStore`) and implemented in `FleetGo.Mobile`, the one project with a platform to run
on and the MAUI workload it needs - the same "abstraction here, platform-specific implementation in
the app" shape `ApiSettings`/`IConnectivity` already used for `Connectivity.Current`. That keeps
`FleetGo.Mobile.Core` (and the tests referencing it) building with nothing but the plain .NET SDK.

### Session restore, sign-out, and where they live

`LoginPage.OnAppearing` calls `IAuthenticationService.TryRestoreSessionAsync()` before showing the
form - a signed-in driver who closes and reopens the app should not have to sign in again every
time. This lives in the page's code-behind rather than the view model on purpose: it is a
navigation decision ("skip this page entirely"), and `LoginViewModel` otherwise knows nothing about
Shell or navigation, matching how `HomeViewModel` never references a page.

## Phase 3: OTP and biometric unlock

### OTP hashing: a per-row random salt plus a server-side pepper, not a plain hash

A 6-digit code has a keyspace of one million. Hashing it the way a password is hashed (a
per-row salt alone, fed through a slow KDF) is still not enough: it makes precomputed rainbow
tables useless, but a leaked `OtpCodes` table can still be brute-forced offline in well under a
second per row, salt included, because the attacker only has to try a million guesses. `OtpService`
instead computes `HMAC-SHA256(key: Otp:HashingKey, message: salt || code)` - the server-side key
(`Otp:HashingKey`, validated at startup the same way `Jwt:SigningKey` is) is what an attacker with
only the database does not have, so offline brute force is not an option regardless of the code's
keyspace. The random salt is still there too, so two identical codes issued to different users (or
the same user at different times) never hash identically. Comparison uses
`CryptographicOperations.FixedTimeEquals`, not `==`, so timing does not leak how many leading bytes
of a guess were correct.

### `IOtpSender`: an abstraction with a fail-fast production guard, not a stub

The API needed *something* to send an OTP through to demonstrate and test the flow, without adding
a paid SMS dependency (Twilio or similar) that was not already part of this repository.
`IOtpSender` is the seam a real provider plugs into later; `DevelopmentOtpSender` (which logs the
code, clearly marked `[DEV-ONLY OTP - NEVER LOGGED IN PRODUCTION]`) is registered only when the
host environment is Development or Testing - `Program.cs` throws at startup if nothing else is
registered outside those environments, so shipping this to production without wiring in a real
sender is a startup crash, not a silent plaintext-OTP-in-the-logs incident.

### Two independent layers of OTP throttling

`OtpService` enforces its own per-user/per-purpose resend cooldown (`Otp:ResendCooldownSeconds`) -
this is what stops a driver, or an attacker who knows a phone number, from requesting a fresh code
before the last one even expires, regardless of which IP the request comes from. Separately,
`/api/v1/auth/otp/request` and `/verify` are wrapped in ASP.NET Core's built-in
`Microsoft.AspNetCore.RateLimiting` middleware, partitioned by caller IP, with a stricter window on
`/verify` than `/request` (`Otp:VerifyRateLimit`/`Otp:RequestRateLimit`) - this is what stops an
attacker from brute-forcing a 6-digit code by spraying verification attempts across many accounts.
Neither layer alone covers both threats; both are cheap enough (no extra NuGet package - the
rate-limiting middleware has shipped in the shared framework since .NET 7) that there was no reason
to pick just one.

### OTP verification issues a normal session, not a second authentication mechanism

`POST /api/v1/auth/otp/verify` calls the same `IAuthService.IssueSessionForUserAsync` that
password login uses internally (a thin wrapper over the existing private token-issuance method) -
a driver who signs in with a code gets the exact same access/refresh token pair, on the exact same
claims, as a driver who signs in with a password. There is one authenticated session shape in this
system, not one per login method.

### Biometric unlock is a local UI gate, not a second way to prove identity to the server

This is the most important constraint in this phase: biometrics never talk to the API and never
mint a token. `IBiometricUnlockCoordinator.TryUnlockAsync` (called once, at app launch, before
`LoginPage` decides whether to show the login form) checks the driver's opt-in preference, then
that a session is actually stored locally, then runs a live biometric challenge through
`IBiometricAuthenticator`, and only on success calls the same `TryRestoreSessionAsync()` every app
launch already used - the same server-verified refresh call, not a shortcut around it. A device
without any stored session, or one where biometrics are not enrolled, simply never gets to try; a
device that fails or cancels a biometric challenge is *not* allowed to fall through to a plain
session restore (see `LoginPage.OnAppearing`) - otherwise "opting into biometric unlock" would not
actually gate anything, since a cancelled prompt could just be waved through.

### The biometric preference lives in `Preferences`, not Keychain/Keystore

`IBiometricPreferenceStore` stores exactly one boolean - "this driver wants biometric unlock on
this device" - and nothing else. It is not a secret (knowing it is `true` reveals nothing an
attacker could use; the actual session tokens stay in `ISecureTokenStore`, untouched by this
phase), so `Microsoft.Maui.Storage.Preferences` is the right level of protection - reaching for
Keychain/Keystore for a UI preference would overstate what is actually being protected.

### Android: SMS Retriever, never `READ_SMS`/`RECEIVE_SMS`

`AndroidOtpAutofillListener` uses the Google Play Services SMS Retriever API, which hands the app
the text of exactly one incoming message - only if it ends with this app's own signature hash - and
nothing else in the device's inbox. `AndroidManifest.xml` requests no SMS permission at all for
this: `READ_SMS`/`RECEIVE_SMS` would let the app read every message on the device, which is far
more than "autofill this one code" needs and is exactly the kind of permission overreach a driver
(and an app store review) would be right to flag. Manual entry works identically whether or not the
retriever ever fires (timeout, a differently-formatted message, Play Services unavailable).

### iOS/Mac Catalyst: the one-time-code keyboard, not SMS access

iOS has no equivalent in-app SMS API this app could safely use, and the platform's own answer is
different from Android's: `UITextContentType.OneTimeCode` on the code entry field turns on the
system QuickType keyboard suggestion, which reads the incoming message at the OS level, entirely
outside this app's process. `MauiProgram.ConfigureOtpAutofillKeyboard` wires this up for exactly the
one `Entry` marked `StyleId="OtpCodeEntry"` via a MAUI handler mapping; app code never sees the
message. Manual entry remains the fallback if the suggestion does not appear.

## Phase 4A: fleet operations backend

Phase 4A adds the server-side data model and API for fleet operations - vehicles, customers,
routes, stops and packages - with no mobile UI yet (that is Phase 4B) and no GPS tracking, proof of
delivery, barcode scanning, or offline sync (all later phases; see "Explicitly out of scope" below).

### Domain shape: an ownership tree rooted at the authenticated driver

```
User / Driver ──┬── Vehicle           (optional assignment, many vehicles can be unassigned)
                 └── Route ── Stop ──┬── Customer   (shared reference data, not owned)
                                      └── Package
```

`Vehicle` and `Route` both hang directly off `Driver` - the existing Phase 2 identity, not a new
"fleet user" concept - because the JWT already carries a `driver_id` claim for exactly this
purpose (see "Phase 2: authentication" above). `Stop` and `Package` do not carry their own driver
reference at all: a stop's owner is its route's driver, and a package's owner is its stop's route's
driver. This is deliberate - a route's driver is the single place ownership is recorded, so
reassigning a route (if a later phase ever allows that) automatically reassigns every stop and
package on it, rather than requiring three tables to be kept in sync.

`Customer` is the one entity in the tree with no owner at all: the same customer legitimately
appears on different drivers' routes over time (a recurring delivery address), so it is modelled as
shared reference data - any authenticated caller can list, view, create or edit a customer, the same
way `Auth`'s own account data works for everyone who is signed in. `Vehicle` sits in between: every
authenticated caller can see the whole fleet (a driver picking a vehicle for tomorrow's route needs
to see vehicles other drivers aren't using), but a vehicle can only be *assigned* to the caller's own
driver profile - never to someone else's - so an id in a request body can't be used to hand a
vehicle to a driver who never agreed to take it. `VehicleEndpoints` enforces this as a 403, not a
validation error, because it is an authorization rule, not a data-shape rule.

This shape is chosen so later phases (GPS breadcrumbs, delivery status transitions, proof of
delivery, barcode scanning, offline sync) attach to `Route`/`Stop`/`Package` as new tables or columns
without changing who owns what.

### Delete behaviour: `Restrict`/`SetNull`, never cascade, for the same reason as `RefreshToken`

Every foreign key in the new schema is configured explicitly in its `IEntityTypeConfiguration<T>`
(`Data/Configurations/*Configuration.cs`), following the precedent `RefreshTokenConfiguration`
already set for `ReplacedByTokenId` (Phase 2): a required reference (`Route.DriverId`,
`Stop.RouteId`, `Stop.CustomerId`, `Package.StopId`) uses `DeleteBehavior.Restrict`, and an optional
one (`Vehicle.DriverId`, `Route.VehicleId`) uses `DeleteBehavior.SetNull`. Nothing cascades. A route
is operational history - proof a driver worked a given day, what was on it, whether it was
completed - and letting a driver or vehicle deletion silently delete that history out from under an
audit would be a bug, not a convenience. There is deliberately no delete endpoint for any of the
five entities either; retiring a vehicle, cancelling a route, or failing a package are all status
changes (`VehicleStatus.Retired`, `RouteStatus.Cancelled`, `PackageStatus.Failed`, ...), never row
deletion, for the same reason.

### Status enums: duplicated across the `Shared`/`API` boundary on purpose

`FleetGo.Shared` has zero project references (see "Why a shared project at all" above) - it is a
plain contracts library both the API and the mobile app depend on, not the other way around. That
means the four status enums (`VehicleStatus`, `RouteStatus`, `StopStatus`, `PackageStatus`) each
exist twice: once as the persisted, EF-mapped type in `FleetGo.API.Data.Entities`
(`.HasConversion<string>().HasMaxLength(20)` - stored as text, not an integer, so the column reads
sensibly in a database tool without a lookup table), and once as the wire-contract type in
`FleetGo.Shared.Contracts.Fleet` (`[JsonConverter(typeof(JsonStringEnumConverter))]`, for the same
reason - a mobile developer reading a captured request body should see `"status": "InProgress"`,
not `"status": 1`). Every endpoint file that needs both aliases the wire-contract namespace
(`using Contracts = FleetGo.Shared.Contracts.Fleet;`) and maps explicitly between the two with a
small `switch` expression, always performed in memory after a query is materialised - never inside
an EF LINQ `.Select()`, where a custom mapping call would risk an untranslatable-expression failure
at runtime.

### Pagination: one shape, enforced in the database, never negotiable per endpoint

Every list endpoint returns the same `PagedResponse<T>` shape:

```json
{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 100, "totalPages": 5 }
```

`Pagination/PageRequest.cs` validates `page`/`pageSize` once (`page >= 1`, `1 <= pageSize <= 100`,
defaulting to page 1 / size 20) and returns a structured `ValidationProblem` on failure rather than
letting each endpoint hand-roll its own range check. `Pagination/PagedQueryExtensions.ToPagedResponseAsync`
then does the count and the page of rows as two database queries (`CountAsync`, then
`Skip().Take().ToListAsync()`) against a still-unmaterialised `IQueryable<T>` - the caller's query
must reach that method without an earlier `ToList()`/`ToArray()`, or paging would happen in memory
against a fully-loaded table instead of in SQL. Every list endpoint's query is also filtered with
`AsNoTracking()` and projects straight to an anonymous shape (or directly to the response record for
`Customer`, which has no enum to map) rather than `Include()`-ing full entity graphs, so a page of
100 rows never pulls in more than the columns the response actually needs.

### Filtering and sorting: explicit per entity, not a query language

Each entity supports exactly the filters called for by its own shape - Vehicles: status; Customers:
name search; Routes: status, route date, and a `sort` parameter (`routeDate`, `status`,
`routeNumber`, each with an optional `-` prefix for descending); Stops: status, customer-name
search, and `sort=sequence`/`-sequence`; Packages: status and tracking-number search - rather than a
generic filter/sort DSL that would accept arbitrary field names. `Filtering/QueryParsing.cs` parses
the non-string filters (an enum, a `DateOnly`) from their raw query-string value by hand, because
letting ASP.NET Core's implicit model binding fail on a bad `status=Sold` produces a generic,
unhelpful 400; parsing it explicitly produces a field-attributed `ValidationProblem` entry instead
(`{"errors": {"status": ["'Sold' is not a valid VehicleStatus. Expected one of: ..."]}}`). A search
filter (customer name, tracking number) is a plain `string.Contains(...)`, translated to a SQL
`LIKE` by EF Core - deliberately not a full-text or fuzzy match, which this phase has no requirement
for.

### Authorization: ownership derived from the token, 404 instead of 403 across drivers

The rule the rest of this section serves: **a driver can never read, list, or modify another
driver's route, stop, or package by guessing or changing an id.** `Auth/ClaimsPrincipalExtensions.GetDriverId()`
reads the `driver_id` claim already issued at login (Phase 2); every route/stop/package endpoint
uses that value as the *only* source of ownership, never a value from the request body or URL.
Concretely:

- `RouteEndpoints` scopes every list/get/update to `route.DriverId == callerDriverId`. A route id
  that belongs to another driver and a route id that does not exist at all both produce **404**, not
  403 - returning 403 would itself leak the information that the id belongs to *someone*, just not
  the caller, which is exactly the kind of enumeration a multi-driver API should not allow.
- `StopEndpoints` and `PackageEndpoints` apply the same rule one (respectively two) hops removed:
  a stop is reachable only through a route the caller owns, and a package only through a stop whose
  route the caller owns. Listing without a `routeId`/`stopId` filter returns results across every
  route/stop the caller *does* own (a join, not a second round trip); listing *with* one 404s
  immediately if the caller doesn't own that parent, before it ever executes the inner query.
- An account with no driver profile at all (a future dispatch/back-office role - see `Driver`'s own
  doc comment) gets **403** from every route/stop/package endpoint (`FleetProblems.Forbidden`,
  `"This account has no driver profile."`) rather than a confusing empty list or a 404, since the
  problem there is who they are, not which resource they asked for.
- `Vehicle` and `Customer` have no per-row ownership check on read (they are shared, as above);
  `Vehicle`'s write-time check (assign only to yourself) is the one 403 that is about the request
  body rather than the URL.

Duplicate identifiers - a repeated `RegistrationNumber`, `RouteNumber`, `(RouteId, Sequence)`, or
`TrackingNumber` - are enforced by both a unique index (`*Configuration.cs`) and a pre-flight
`AnyAsync` check in the endpoint, returned as **409 Conflict** via a shared `FleetProblems.Conflict`
helper, distinct from the 400s that field-level validation produces.

### Explicitly out of scope

This phase is the backend only. It does not add - and nothing in it should be read as claiming -
a dashboard or any mobile UI for these endpoints (Phase 4B), GPS tracking or live location, delivery
status changes driven by proof-of-delivery capture, barcode/QR scanning, BLE, camera or signature
capture, push notifications, payments, or offline/background sync. Those remain future-roadmap
items; see docs/roadmap.md.

### Known gap surfaced, not introduced, by this phase: the missing `OtpCode` migration

`OtpCode` (its entity, `IEntityTypeConfiguration<T>`, and `DbSet`) was added in Phase 3, but no
migration for it was ever generated - the only migration in the repository before this phase,
`InitialAuth`, predates `OtpCode` entirely, and docs/roadmap.md already tracked this as a TODO
("run `dotnet ef migrations add AddOtp` locally"). Since a migration is a diff against the model the
*previous* migration describes, `AddFleetOperations` (this phase's migration) necessarily also
creates the `OtpCodes` table - it is the first migration generated since `OtpCode` was added, not a
Phase 4A change to `OtpCode` itself. Nothing about `OtpCode`'s entity or configuration changed here.

## Testing

`FleetGo.API.Tests` boots the real host in memory with `WebApplicationFactory`, so routing, DI,
serialisation and middleware are all exercised together. Nothing of our own code is mocked; the
tests fail if any of those pieces is misconfigured, which is precisely the class of bug unit tests
on handlers would miss. Phase 4A's `VehicleEndpointsTests`, `CustomerEndpointsTests`,
`RouteEndpointsTests`, `StopEndpointsTests` and `PackageEndpointsTests` follow the same pattern
`AuthEndpointsTests` established, plus `FleetTestSupport` for the seeding helpers (a driver, a
second independent driver, a customer, a route, a stop, a package) that every one of them needs to
exercise the cross-driver isolation rules above.

`FleetGo.API.Tests` boots the real host in memory with `WebApplicationFactory`, so routing, DI,
serialisation and middleware are all exercised together. Nothing of our own code is mocked; the
tests fail if any of those pieces is misconfigured, which is precisely the class of bug unit tests
on handlers would miss.

`FleetGo.Mobile.Tests` targets plain `net10.0` and covers the shared client, serialisation, and
- since Phase 2 - `FleetGo.Mobile.Core`'s view models and session logic with a stubbed
`HttpMessageHandler`, so it runs in CI without an emulator.

### Test runner: Microsoft.Testing.Platform, not VSTest

xUnit v3 hosts its own runner inside the test executable (hence `<OutputType>Exe</OutputType>`),
and the .NET 10 SDK removed the bridge that let `dotnet test` drive such a runner through the
legacy VSTest target. So the test projects reference neither `Microsoft.NET.Test.Sdk` nor
`xunit.runner.visualstudio` - both are VSTest infrastructure - and instead set
`UseMicrosoftTestingPlatformRunner`, with `dotnet test` opted into MTP mode by the `test`
section of `global.json`.

The trade-off worth knowing: keeping the VSTest packages is the transitional option xUnit
suggests for teams whose IDEs still drive VSTest. Since this repository targets .NET 10 only,
where that path no longer works from the CLI, carrying both runners would mean maintaining a
configuration that cannot actually run.

### Resolved gap: view model tests

Phase 1 left this open (a `net10.0` test project cannot reference a MAUI app project, because the
app only targets platform heads) and named two options. **Option 1 - extract a `net10.0` library
for view models and services, referenced by both the app and the tests - is what `FleetGo.Mobile.Core`
is**, taken at the point `LoginViewModel` justified it (see "Mobile" above). `HomeViewModel` stays in
`FleetGo.Mobile` for now: it has no logic yet worth testing on its own, so moving it too would be
ceremony without benefit; it can join `FleetGo.Mobile.Core` the same way if that changes.

## Deliberately not done yet

Central package management, a `Directory.Packages.props`, Serilog, MediatR, a repository layer, an
`Application`/`Domain`/`Infrastructure` split, a full ASP.NET Core Identity setup (see "Phase 2:
authentication" above for why `PasswordHasher<TUser>` alone was enough), rate limiting on
*password* login attempts (Phase 3 added IP-partitioned rate limiting to the OTP endpoints
specifically - see "Phase 3: OTP and biometric unlock" above - `POST /api/v1/auth/login` itself
still has none; extending the same middleware to it is straightforward and worth doing before this
API is internet-facing), and a reactive "refresh on 401, retry once" HTTP handler (`GetAccessTokenAsync` refreshes
proactively before a token's expiry instead - see that method's comments - which covers the common
case without the added complexity of safely avoiding recursion between a retry handler and the
refresh call itself; the roadmap's "authenticated handler that refreshes on 401" is still worth
adding as defence in depth against clock skew between device and server). Each is a reasonable
choice in a codebase whose shape justifies it. None of them are justified by the code that exists
today, and adding them early buys ceremony rather than clarity. They are revisited when a phase
creates the problem they solve - and central package management specifically has a wrinkle with
MAUI, whose `$(MauiVersion)` is supplied by the workload.

## Security ground rules

The repository is public, so:

- No secrets, keys, connection strings with credentials, certificates or tokens are ever committed.
  Local development secrets go in `dotnet user-secrets` (the API project has a `UserSecretsId`);
  deployed environments use environment variables. As of Phase 3, this covers `Jwt:SigningKey`,
  `Otp:HashingKey` and `ConnectionStrings:FleetGoDatabase` specifically - see
  docs/development-setup.md.
- `appsettings.*.Local.json`, `.env`, `*.keystore`, `*.p12` and friends are in `.gitignore`.
- Payment work uses sandbox credentials only, supplied at runtime, never checked in.
- Passwords are never stored or logged in plaintext (`PasswordHasher<TUser>`); refresh tokens are
  never stored in plaintext either (SHA-256 hash only) - see "Phase 2: authentication" above. As of
  Phase 3, one-time codes follow the same rule - only a salted HMAC hash is ever stored, and the
  plaintext code is only ever logged by `DevelopmentOtpSender`, which cannot register outside
  Development/Testing (see "Phase 3: OTP and biometric unlock" above).

