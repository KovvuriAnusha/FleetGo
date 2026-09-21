# FleetGo

[![CI](https://github.com/KovvuriAnusha/FleetGo/actions/workflows/ci.yml/badge.svg)](https://github.com/KovvuriAnusha/FleetGo/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A driver and delivery application built with **.NET MAUI 10** and **ASP.NET Core 10**, developed in
public as an engineering portfolio project.

FleetGo is the kind of app a courier or field-service company runs on: drivers sign in, pick up a
route, work through their stops, capture proof of delivery, and keep working when the signal drops.
It is being built phase by phase, with each phase adding one coherent slice of a real product -
authentication, routes and stops, location tracking, offline-first sync, proof of delivery - rather
than a collection of disconnected demos.

> **Original work.** Every line here is written from scratch for this repository. It contains no
> code, data, naming, architecture or screenshots from any employer or client project, and no
> secrets, keys or credentials of any kind.

---

## Status

| Phase | Scope | State |
|-------|-------|-------|
| 1 | Solution foundation: projects, DI, OpenAPI, health checks, tests, CI | ✅ Complete |
| 2 | Authentication: JWT + refresh tokens with rotation, EF Core + SQL Server, secure token storage | ✅ Complete |
| 3 | One-time-code sign-in, Android SMS autofill, biometric unlock | ✅ Complete |
| 4A | Fleet operations backend: vehicles, customers, routes, stops, packages | ✅ Complete |
| 4B | Driver mobile experience: dashboard, route list, route detail, stop detail | ✅ Complete |
| 4C | Integration, polish and release configuration | ✅ Complete |
| 5+ | Offline-first sync, proof of delivery, location tracking, payments | 📋 Planned |

The full plan is in [docs/roadmap.md](docs/roadmap.md), and the reasoning behind the design
decisions is in [docs/architecture.md](docs/architecture.md).

---

## Technology

**Mobile** — .NET 10, .NET MAUI 10, C#, XAML, MVVM (CommunityToolkit.Mvvm), dependency injection,
Shell navigation, compiled bindings. `FleetGo.Mobile.Core` (added in Phase 2) holds view models and
session/token logic in a plain, MAUI-free library so they run under regular unit tests.

**Backend** — ASP.NET Core 10 Minimal APIs, OpenAPI (Scalar UI), health checks (including a
readiness check against the database), RFC 9457 `problem+json` error responses. Entity Framework
Core with the SQL Server provider, JWT bearer authentication, and rotating refresh tokens were
added in Phase 2.

**Testing** — xUnit v3; integration tests that boot the real API host in memory through
`WebApplicationFactory` (SQLite in-memory for anything that touches the database), plus unit tests
for the client-side code, including the Phase 2 view models and session/token handling.

---

## Solution structure

```
FleetGo/
├── src/
│   ├── FleetGo.Shared/        Contracts, routes, JSON settings and the typed API client
│   ├── FleetGo.API/           ASP.NET Core Web API host
│   ├── FleetGo.Mobile.Core/   View models and session/token logic (no MAUI dependency)
│   └── FleetGo.Mobile/        .NET MAUI app (Android, iOS, Mac Catalyst)
├── tests/
│   ├── FleetGo.API.Tests/     In-memory integration tests for the API
│   └── FleetGo.Mobile.Tests/  Unit tests for the shared client-side code
├── docs/                      Architecture decisions, roadmap, setup notes
├── .github/workflows/ci.yml   Build + test on every push and pull request
├── Directory.Build.props      Settings shared by every project
├── global.json                Pins the .NET SDK band
└── FleetGo.sln
```

Four source projects, two test projects, and no layer that exists only to satisfy a diagram.
The reasoning behind each one is written up in [docs/architecture.md](docs/architecture.md).

---

## Getting started

### Prerequisites

- .NET SDK 10.0.100 or later
- .NET MAUI workloads: `dotnet workload install maui`
- For Android: JDK 17 and the Android SDK (installed with Visual Studio or Android Studio)
- For iOS / Mac Catalyst: macOS with Xcode
- SQL Server, for Phase 2 onward - see [docs/development-setup.md](docs/development-setup.md#database)

### Run the API

As of Phase 2 the API needs a JWT signing key and a database connection string configured via
`dotnet user-secrets` before it will start - see
[docs/development-setup.md](docs/development-setup.md#required-local-configuration-phase-2) for the
exact commands.

```bash
dotnet run --project src/FleetGo.API
```

| What | Where |
|------|-------|
| Interactive API reference | <http://localhost:5266/scalar/v1> |
| OpenAPI document | <http://localhost:5266/openapi/v1.json> |
| Health report | <http://localhost:5266/health> |

### Run the mobile app

The app's API address is a build input. Debug builds fall back to the local development API;
Release builds require `-p:FleetGoApiBaseAddress=...` and fail without it - see
[docs/development-setup.md](docs/development-setup.md#pointing-the-app-at-an-api).

```bash
# Android emulator
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-android

# iOS simulator
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-ios

# Mac Catalyst
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-maccatalyst
```

Start the API first: the app signs in against it, and the Phase 1 landing page still reports
whether it is reachable. The Android emulator reaches the host machine at `10.0.2.2`, not
`localhost` - see [docs/development-setup.md](docs/development-setup.md) if the app cannot connect.

### Run the tests

```bash
dotnet test --project tests/FleetGo.API.Tests
dotnet test --project tests/FleetGo.Mobile.Tests
```

Tests run on **Microsoft.Testing.Platform** (xUnit v3 self-hosts its runner), which the
.NET 10 SDK is pointed at by the `test` section of `global.json`. That is why the project
path is passed as `--project` rather than positionally.

---

## License

[MIT](LICENSE) © 2026 Anusha Kovvuri

