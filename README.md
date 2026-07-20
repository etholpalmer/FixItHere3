# FixItHere.Demo

Proof-of-concept for the FixItHere mobile-services marketplace.
See [`docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`](docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md).

## Run the backend + demo control panel

```bash
dotnet run --project src/Backend.Api
```

Then open <http://localhost:5162/dev> — press **Start Demo** to watch the full
book → accept → travel → chat → arrive → work → pay → rate flow, live on the map.

Every startup resets the database to identical seed data
(7 services, 20 customers, 20 providers, 80 jobs, ratings, messages).

## Run the Customer app (iOS Simulator)

```bash
# Boot a simulator once, then build and run onto it:
xcrun simctl boot "iPhone 17 Pro" && open -a Simulator
dotnet build -t:Run -f net10.0-ios src/Customer.Mobile
```

Requires the backend running (above). Log in as John/Mary/Steve/Susan/Bob.
Use the /dev console as the "provider side" to accept/drive jobs, or press
Start Demo there for the fully scripted flow.

Android emulator: the app auto-targets http://10.0.2.2:5162.

## Run the Provider app (iOS Simulator)

Run this on a *second* simulator so both apps are visible side by side — that
pairing is the demo.

```bash
xcrun simctl boot "iPhone 17" && open -a Simulator
dotnet build -t:Run -f net10.0-ios src/Provider.Mobile
```

Requires the backend running (above). Log in as one of:
- **Mike's Plumbing** (password: Provider1!)
- **Joe Electric** (password: Provider1!)
- **Rapid Tire Repair** (password: Provider1!)
- **Elite HVAC** (password: Provider1!)

From **DevSettings**, press **Start Demo** to watch the fully scripted two-app flow
(booking, acceptance, travel, chat, work completion, payment, rating).
Customer app also has a **Start Demo** button in DevSettings.

## Test

```bash
dotnet test
```

## Projects

- `src/Shared` — pure F# domain: types, DTOs, Job state machine
- `src/Backend.Api` — F# Minimal API + EF Core/SQLite + SignalR + `/dev` console
- `src/Customer.Mobile`, `src/Provider.Mobile` — Fabulous MAUI apps (Plans 2–3, not yet built)

## Notes

- **.NET 10** SDK (the design doc says net8.0; net10 is what the toolchain here provides — same code).
- Backend listens on `http://localhost:5162` — set by `applicationUrl` in
  `src/Backend.Api/Properties/launchSettings.json`, which `dotnet run` applies and which
  takes precedence over `ASPNETCORE_URLS`. To override the port you must edit that file
  or run with `--no-launch-profile` plus `ASPNETCORE_URLS`.
- **Do not use port 5000 on macOS.** It is occupied by the AirPlay Receiver
  (ControlCenter), which answers with HTTP 403 — requests never reach the API. The
  mobile apps' `Config.baseUrl` therefore targets 5162 to match the backend.
- Dev endpoints (`/dev`, `/dev/reset`, `/dev/demo/start`) are mapped in the
  **Development** environment only.
