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

- **Contracts** (`ApiInfoResponse`, `HealthReportResponse`) - the payload shapes.
- **`ApiRoutes`** - the URLs, as constants. The API maps them; the client calls them; the tests
  assert against them. A typo cannot affect only one side.
- **JSON settings** - one `JsonSerializerOptions` and one source-generated serializer context, so
  the bytes the server writes are read by the client with identical rules.
- **`FleetGoApiClient`** - a typed client over `HttpClient` that speaks those contracts.

It deliberately has **no NuGet dependencies** and no reference to ASP.NET Core, EF Core or MAUI.
That constraint is what makes it safe for a mobile app to consume, and it is the line that keeps
"shared" from quietly becoming "everything".

### Why the API client lives in Shared rather than in the mobile app

It is a plain `HttpClient` wrapper with no platform dependency, so it costs nothing to host it next
to the contracts it speaks - and doing so means it can be unit tested from an ordinary `net10.0`
test project, without an emulator. Configuration (base address, timeout, retry policy) is *not* in
the client: it is applied by whoever registers it, which in the app is `MauiProgram`.

## Backend

### Minimal APIs over MVC controllers

The endpoints in this application are thin: validate input, call a service, return a result. Minimal
APIs express that with less ceremony, start faster, and route registration groups naturally by
feature (`MapSystemEndpoints`, later `MapAuthEndpoints`, `MapRouteEndpoints`). Controllers earn
their keep when you need filters, model binders and conventions layered on top - if a later phase
needs that, a controller can be added alongside; the two coexist in one host.

### Source-generated JSON on both sides

The server registers the same `FleetGoJsonSerializerContext` the mobile app uses. On the server it
is a small startup and throughput win. On mobile it matters more: release builds are trimmed and iOS
builds are AOT compiled, and reflection-based serialisation is exactly what trimming removes.
Generating the serialisation code at compile time sidesteps the problem instead of patching it with
trimmer hints later.

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
outage should stop traffic being routed to the instance, but restarting the container will not fix it.

### Errors as `problem+json`

`AddProblemDetails()` with `UseExceptionHandler()` and `UseStatusCodePages()` means every failure -
unhandled exception, 404, 405 - reaches the client as one predictable JSON shape (RFC 9457) instead
of an HTML error page the mobile app cannot parse. Custom exception-to-status mapping is added in
the phase that introduces domain exceptions.

### API versioning

Version 1 is expressed in the path (`/api/v1/...`) via the shared `ApiRoutes.Base` constant. A
formal versioning library (`Asp.Versioning`) is worth adding when a v2 actually exists and two
versions must be served side by side; adding it now would be configuration without a consumer.

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
in during the resilience phase.

### Platform reach-back to the API

Each platform has a different idea of "the machine running the API": the Android emulator uses
`10.0.2.2`, the iOS simulator and Mac Catalyst use `localhost`. `ApiSettings` resolves this once.
Android's cleartext-HTTP block is opened for those two hosts only, via a network security config -
not by switching cleartext on globally.

## Testing

`FleetGo.API.Tests` boots the real host in memory with `WebApplicationFactory`, so routing, DI,
serialisation and middleware are all exercised together. Nothing of our own code is mocked; the
tests fail if any of those pieces is misconfigured, which is precisely the class of bug unit tests
on handlers would miss.

`FleetGo.Mobile.Tests` targets plain `net10.0` and covers the shared client and serialisation with a
stubbed `HttpMessageHandler`, so it runs in CI without an emulator.

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

### Known gap: view model tests

A `net10.0` test project cannot reference a MAUI app project, because the app only targets platform
heads (`net10.0-android` and friends). Today's view model is thin enough that this costs nothing,
but as soon as view models carry real logic, one of these applies:

1. **Extract `FleetGo.Mobile.Core`** - a `net10.0` library (with `UseMaui`) holding view models and
   services, referenced by both the app and the tests. Conventional, and the cost is one more project.
2. **Add a `net10.0` head to the app** so the test project can reference it. No extra project, but
   the `.csproj` grows conditions and the Platforms folder has to be excluded from that head.

Option 1 is the plan, taken at the point the logic justifies it rather than pre-emptively.

## Deliberately not done yet

Central package management, a `Directory.Packages.props`, Serilog, MediatR, a repository layer, an
`Application`/`Domain`/`Infrastructure` split. Each is a reasonable choice in a codebase whose shape
justifies it. None of them are justified by five files, and adding them early buys ceremony rather
than clarity. They are revisited when a phase creates the problem they solve - and central package
management specifically has a wrinkle with MAUI, whose `$(MauiVersion)` is supplied by the workload.

## Security ground rules

The repository is public, so:

- No secrets, keys, connection strings with credentials, certificates or tokens are ever committed.
  Local development secrets go in `dotnet user-secrets` (the API project has a `UserSecretsId`);
  deployed environments use environment variables.
- `appsettings.*.Local.json`, `.env`, `*.keystore`, `*.p12` and friends are in `.gitignore`.
- Payment work uses sandbox credentials only, supplied at runtime, never checked in.
