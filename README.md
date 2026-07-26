# FixItHere.Demo

Prototype of the FixItHere mobile-services marketplace: a customer books a
provider, the provider drives to them, they chat live, the job completes, money
moves, and both rate. Two iOS apps over one F# backend, paced by an
operator-controlled demo clock.

- Design spec: [`docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`](docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md)
- Engineering journal — what broke, what was learned, what was traded off:
  [`LESSONS-LEARNED.md`](LESSONS-LEARNED.md)

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

Requires the backend running (above). The sign-in form is prefilled with the
primary demo account — `john.reyes@gmail.com` / `Customer1!` — so it is one tap. Other
seeded customers follow the same `first.last@domain` shape
(`mary.okonkwo@outlook.com`, `steve.lindqvist@icloud.com`,
`susan.chaudhry@yahoo.ca`, `bob.tremblay@gmail.com`); `GET /customers` lists them all.
The provider side is the **Provider app** (below), run on a second simulator —
the two apps side by side are the demo. The `/dev` console is the operator's
panel rather than a second actor: it owns the demo clock and can drive a fully
scripted run (**Start Demo**) when you want the flow to play hands-free.

The console also owns the **demo clock**. The world starts at 2026-01-01 and
runs at 1x; pause it to talk over a beat, run it at 60x to compress a half-hour
wait into thirty seconds, or skip to two minutes before the next promised
arrival to re-stage a beat on demand. The server holds the clock and pushes the
*map* (anchor + rate + running) rather than the time, so every client
extrapolates locally — nothing polls, and no client owns a timer that a moved
deadline could strand.

Android emulator: the app auto-targets http://10.0.2.2:5162.

## Run the Provider app (iOS Simulator)

Run this on a *second* simulator so both apps are visible side by side — that
pairing is the demo.

```bash
xcrun simctl boot "iPhone 17" && open -a Simulator
dotnet build -t:Run -f net10.0-ios src/Provider.Mobile
```

Requires the backend running (above). Prefilled with `contact@mikesplumbing.ca`
/ `Provider1!`. Provider emails are derived from the business name:
- **Mike's Plumbing** — `contact@mikesplumbing.ca`
- **Joe Electric** — `contact@joeelectric.ca`
- **Rapid Tire Repair** — `contact@rapidtirerepair.ca`
- **Elite HVAC** — `contact@elitehvac.ca`

All demo accounts share one password per role (`Customer1!` / `Provider1!`).
This is deliberately *not* a security mechanism — see `src/Backend.Api/Auth.fs`.
It exists so a sign-in that is tested during a demo behaves like a real one.

Demo controls live in the `/dev` console, not in the apps — Start Demo, the
route walk, provider position and message injection are all driven from there.
The apps ship no developer surface: an operator control visible on a product
screen is the loudest tell there is.

## Test

```bash
dotnet test
```

## Local CI

CI runs on this machine, not in the cloud: `scripts/ci-local.sh` mirrors the
paused GitHub workflow's gates (the four test suites + the iOS view-code
compile gate) and `.githooks/pre-push` runs it before every push. Activate the
hook once per clone:

```bash
git config core.hooksPath .githooks
```

Options: `--tests-only`, `--full` (adds the full iOS package build),
`--linux` (runs the test suites in a Linux container via Docker — the
platform leg the cloud run used to provide). Escape hatch: `SKIP_CI=1 git push`.
The GitHub workflow remains manually runnable from the Actions tab.

## Projects

- `src/Shared` — pure F# domain: types, DTOs, Job state machine, demo clock
- `src/Backend.Api` — F# Minimal API + EF Core/SQLite + SignalR + `/dev` console
- `src/ClientShared` — F# both apps link (not a package): HTTP, the SignalR hub
  client, the Leaflet map page (`MapHtml.fs`), and the shared design tokens
- `src/Customer.Mobile`, `src/Provider.Mobile` — Fabulous MAUI apps (iOS), the
  redesigned consumer surface. Both are built; the two running side by side
  *are* the demo

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
