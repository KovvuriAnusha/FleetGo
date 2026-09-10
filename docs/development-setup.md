# Development setup

## Prerequisites

| Tool | Notes |
|------|-------|
| .NET SDK 10.0.100+ | `global.json` pins the 10.0.1xx band and rolls forward to your latest 10 SDK |
| .NET MAUI workloads | `dotnet workload install maui` (or `maui-android` / `maui-ios` individually) |
| JDK 17 | Required by the Android build |
| Android SDK | Ships with Visual Studio or Android Studio; API level 24+ |
| Xcode | macOS only, for iOS and Mac Catalyst |

Check what is installed:

```bash
dotnet --info
dotnet workload list
```

## Everyday commands

```bash
# Backend + shared + tests (no MAUI workloads needed)
dotnet build tests/FleetGo.API.Tests
dotnet test --project tests/FleetGo.API.Tests
dotnet test --project tests/FleetGo.Mobile.Tests

# Run the API (Scalar UI opens at /scalar/v1)
dotnet run --project src/FleetGo.API

# Build every project, mobile heads included
dotnet build FleetGo.sln

# Run the app
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-android
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-ios
dotnet build src/FleetGo.Mobile -t:Run -f net10.0-maccatalyst
```

`dotnet build FleetGo.sln` requires the MAUI workloads, because the solution includes the mobile
project. Build the individual projects if you only want to work on the backend.

## Running the tests

The test projects run on **Microsoft.Testing.Platform** (MTP): xUnit v3 hosts its own runner
inside the test executable, so there is no VSTest adapter and no `Microsoft.NET.Test.Sdk`.
`global.json` opts `dotnet test` into MTP mode:

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

In MTP mode the project is passed as an option, not positionally:

```bash
dotnet test --project tests/FleetGo.API.Tests          # correct
dotnet test tests/FleetGo.API.Tests/FleetGo.API.Tests.csproj   # legacy VSTest syntax
```

Useful flags: `-c Release`, `--no-build`, `-f net10.0`, `--results-directory`,
`--show-test-results failed`.

### "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform"

This means `dotnet test` ran in VSTest mode against an MTP-based test project. The .NET 10
SDK dropped the VSTest bridge that used to make that work. Check that `global.json` contains
the `test.runner` setting above, that the test project sets
`<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`, and that
neither `Microsoft.NET.Test.Sdk` nor `xunit.runner.visualstudio` is referenced.

## Ports and addresses

The API listens on `http://localhost:5266` (and `https://localhost:7266` under the `https` profile),
configured in `src/FleetGo.API/Properties/launchSettings.json`.

The mobile app resolves the API address per platform in `src/FleetGo.Mobile/Configuration/ApiSettings.cs`:

| Target | Address it uses | Why |
|--------|-----------------|-----|
| Android emulator | `http://10.0.2.2:5266` | The emulator is behind its own NAT; `10.0.2.2` is the host's loopback |
| iOS simulator | `http://localhost:5266` | Shares the host network stack |
| Mac Catalyst | `http://localhost:5266` | Runs on the host |
| Physical device | Your machine's LAN IP | Edit `ApiSettings`; device and machine must be on the same network |

## "The app says it cannot reach the API"

Work through these in order:

1. **Is the API running?** `curl http://localhost:5266/health` should return a JSON health report.
2. **Right address for the target?** See the table above. `localhost` inside an Android emulator
   means the emulator itself, not your Mac.
3. **Cleartext HTTP blocked?** Android permits it only for `10.0.2.2` and `localhost`, via
   `Platforms/Android/Resources/xml/network_security_config.xml`. A different host needs adding there
   (or, better, HTTPS).
4. **Using the HTTPS profile?** A simulator or emulator does not trust the ASP.NET Core development
   certificate by default. Prefer the `http` profile while developing, or run
   `dotnet dev-certs https --trust` and follow the platform-specific trust steps.
5. **Firewall** on the physical-device path - the Mac must accept inbound connections on 5266.

## Secrets

Never commit them. For local development:

```bash
cd src/FleetGo.API
dotnet user-secrets set "SomeSection:SomeKey" "value"
```

Secrets are stored outside the repository, keyed by the `UserSecretsId` in `FleetGo.API.csproj`.
Deployed environments supply the same keys as environment variables.
