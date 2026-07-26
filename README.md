# FixItHere.Demo

Prototype of the FixItHere mobile-services marketplace: a customer books a
provider, the provider drives to them, they chat live, the job completes, money
moves, and both rate. Two iOS apps over one F# backend, paced by an
operator-controlled demo clock.

- Design spec: [`docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`](docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md)
- Engineering journal — what broke, what was learned, what was traded off:
  [`LESSONS-LEARNED.md`](LESSONS-LEARNED.md)

---

## Spin up the whole demo (one command)

```bash
scripts/demo-up.sh
```

This brings up all three surfaces:

- **Control console** — the backend, with the operator panel at <http://localhost:5162/dev>
- **Customer app** — on one simulator (default: *iPhone 17 Pro*)
- **Provider app** — on a second simulator (default: *iPhone 17 Pro Max*), so the
  two sit side by side — that pairing is the demo

It starts the backend (waits until it is healthy), builds and installs both apps
onto their simulators, launches them, and opens the console in your browser. Both
sign-in forms are prefilled, and both prefilled logins **share the soonest job**
(John Reyes ⇄ Mike's Plumbing), so a two-sided flow is one tap away on each app.

```bash
scripts/demo-up.sh --backend     # console only, no simulators/apps
scripts/demo-up.sh --no-build     # skip the rebuild, reinstall the last build (fast)
CUSTOMER_SIM="iPhone 17" PROVIDER_SIM="iPhone 16" scripts/demo-up.sh   # pick devices

scripts/demo-down.sh             # stop the backend (leaves simulators booted)
scripts/demo-down.sh --sims      # also shut the simulators down
```

Requirements: macOS, Xcode, and the .NET 10 SDK with the `maui` workload (the
toolchain the apps already build with). `--backend` mode needs none of the Apple
bits. Runtime pids/logs land in `.demo-run/` (git-ignored); build/run logs there
are where to look if a step fails.

Every backend start **reseeds an identical database** — 7 services, 20 customers,
20 providers, 80 jobs, plus ratings and messages — so every run is the same.

---

## What you can demonstrate today

Everything below works end-to-end and has been driven live on two simulators.
The **customer** and **provider** are the two apps; the **operator** is the
`/dev` console.

### The core two-sided flow
On the apps, tapped by hand, each step crossing to the other phone in real time:

| Step | Who acts | What the other side sees |
|---|---|---|
| **Book** | customer picks a trade → provider → time | job appears in the provider's *Available jobs* |
| **Accept** | provider taps Accept | job leaves *Available*, becomes the provider's *Active job* ("Ready to head out"); customer status flips to **"Accepted — your provider will head out soon"**; provider is now off the market (can't take a second job) |
| **Depart** | provider taps Depart | customer sees "on the way", the honey marker starts moving, the map zooms to fit |
| **Travel** | (automatic, demo-clock paced) | marker glides along the route, ETA + countdown update together on demo time |
| **Arrive → Start work → Complete** | provider taps each | customer status flips live at each step |
| **Pay** | customer settles up | receipt with the **money story** below |
| **Rate** | both rate each other | job closes; the provider's public star average updates |

> **Watching the drive:** the provider's marker moves on *demo* time, not wall time —
> at 1× a real 25-minute trip advances one step every couple of real minutes, so it
> looks frozen. Click **60×** (or **120×**) in the console during the travel beat and
> the marker visibly travels and the map zooms to fit. This is intentional: motion and
> the ETA/countdown share the demo clock, so they can never disagree.

### The signature beats
- **Live chat** — free-text messages cross both ways, with **typing indicators**
  and **"Seen" receipts**. (See `LESSONS-LEARNED.md` on the ~3s typing bubble.)
- **"Running late" reschedule** — provider proposes +10/+15/+30 min; the
  customer's phone lights up with the request; accept retargets both countdowns,
  decline resumes the original no-show clock. The console can drive this
  hands-free via **Start Demo · running late**.
- **The money story** (the marketplace proof) — the two figures differ *for the
  right reasons*: the customer pays subtotal **+ 13% HST**, the provider receives
  subtotal **− 15% platform fee**. Both derive from one subtotal and both add up.
- **Ratings integrity** — a provider rating the customer does **not** move the
  provider's own public average, even though customer and provider id spaces both
  start at 1.
- **Provider availability** — accepting or working a job pauses new requests
  ("On a job — new requests paused"); finishing puts them back online.
- **No-show escalation** — once the grace window past the promised arrival
  elapses, the customer is offered **Report no-show** and the provider's screen
  says the customer can now report them.
- **Coherent world** — real GTA addresses (nothing in the lake), trade-derived
  prices, avatars that never 404, chat timestamps, dated reviews with real names.

### The operator console (`/dev`)
The panel that lets one person run a convincing live demo:

- **Start Demo** / **Start Demo · running late** — a fully scripted run of the
  whole flow (book → … → pay), optionally with the late/reschedule beat.
- **Reset** — reseed the database instantly; both apps drop their stale world and
  refetch (they do not need relaunching).
- **Demo clock** — **Pause** (hold every countdown mid-sentence, toasts included),
  **1× / 10× / 60× / 120×** (compress a half-hour wait into seconds), and
  **Skip to T−2 min on next job** (re-stage the "arriving" beat on demand).
- **Personas / Create job / Book selected provider** — stage bookings from the console.
- **Inject message** (as customer or provider) — drop a chat line into a job.
- **Force payment** — settle a job's payment from the console.
- **Route 0 → 100%** — walk a provider along the route by hand.
- **Events** — a live log of every hub event, so the operator can see the
  system reacting.

> The apps themselves ship **no** developer surface — every one of those controls
> lives only in `/dev`. An operator control visible on a product screen is the
> loudest tell there is.

---

## Accounts

Sign-in forms are prefilled, so each app is one tap in. All demo accounts share
one password per role: **`Customer1!`** / **`Provider1!`**. This is deliberately
*not* a security mechanism (see `src/Backend.Api/Auth.fs`) — it exists so that a
sign-in exercised during a demo behaves like a real one.

- **Customers** — `first.last@domain`: `john.reyes@gmail.com` (the prefilled one),
  `mary.okonkwo@outlook.com`, `steve.lindqvist@icloud.com`, `susan.chaudhry@yahoo.ca`,
  `bob.tremblay@gmail.com`. `GET /customers` lists all 20.
- **Providers** — derived from the business name: `contact@mikesplumbing.ca`
  (prefilled), `contact@joeelectric.ca`, `contact@rapidtirerepair.ca`,
  `contact@elitehvac.ca`. `GET /providers` lists all 20.

## The demo clock

The world starts at 2026-01-01 at 1×. The server holds one authoritative clock
and pushes the *map* of it (anchor + rate + running), not the time — so every
client extrapolates against its own wall clock, **nothing polls, and no client
owns a timer a moved deadline could strand.** Pausing the clock also pauses toast
expiry, which is exactly right when an operator pauses to talk.

---

## Manual / step-by-step (without the script)

```bash
# 1. Backend + console
dotnet run --project src/Backend.Api          # http://localhost:5162/dev

# 2. Customer app — on one booted simulator
xcrun simctl boot "iPhone 17 Pro" && open -a Simulator
dotnet build -t:Run -f net10.0-ios src/Customer.Mobile

# 3. Provider app — on a SECOND simulator (both visible = the demo)
xcrun simctl boot "iPhone 17 Pro Max"
dotnet build -t:Run -f net10.0-ios src/Provider.Mobile
```

`dotnet build -t:Run` targets the booted simulator; with two booted it is
ambiguous, which is exactly why `demo-up.sh` installs to each device by UDID
instead. Android emulator: the apps auto-target `http://10.0.2.2:5162`.

## Test

```bash
dotnet test tests/Shared.Tests
dotnet test tests/Backend.Api.Tests
dotnet test tests/Customer.Mobile.Tests
dotnet test tests/Provider.Mobile.Tests
```

Run the four test projects explicitly — **not** `dotnet test` on the `.slnx`,
which pulls in the mobile TFMs and fails for environment reasons.

## Local CI

CI runs on this machine, not in the cloud: `scripts/ci-local.sh` mirrors the
paused GitHub workflow's gates (the four test suites + the iOS view-code compile
gate) and `.githooks/pre-push` runs it before every push. Activate the hook once
per clone:

```bash
git config core.hooksPath .githooks
```

Options: `--tests-only`, `--full` (adds the full iOS package build), `--linux`
(runs the suites in a Linux container via Docker — the platform leg the cloud run
used to provide). Escape hatch: `SKIP_CI=1 git push`. The GitHub workflow remains
manually runnable from the Actions tab.

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
