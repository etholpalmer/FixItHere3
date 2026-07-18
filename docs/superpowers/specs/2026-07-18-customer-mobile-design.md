# FixItHere.Demo — Customer.Mobile Design (Plan 2)

**Date:** 2026-07-18
**Status:** Approved (brainstorming) — ready for implementation planning
**Parent spec:** [2026-07-17-fixithere-demo-prototype-design.md](2026-07-17-fixithere-demo-prototype-design.md)
**Backend contract:** built and verified in Plan 1 ([plan](../plans/2026-07-18-backend-and-dev-console.md)) — `Shared` DTOs, ~18 REST endpoints, SignalR `DemoHub` at `/hub`, deterministic seed.

## Purpose

The customer-side mobile app for the FixItHere demo: browse the catalog, book a provider, watch them travel on a live map, chat, pay (fake), and rate. Consumes the Plan 1 backend exactly as-is — **zero Backend.Api changes in this plan**.

## Confirmed decisions

| Decision | Choice |
| --- | --- |
| UI framework | **Fabulous.MauiControls 8.0.x** (MVU, F#, no XAML) + `Fabulous.MauiControls.Templates` for scaffolding |
| Target frameworks | `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows` (Windows condition-guarded; not buildable on this Mac — expected). Fallback: if net10 MAUI workload manifests are unavailable, drop this project to the latest available mobile TFMs (net9/net8) — Backend.Api and Shared stay net10.0 |
| Navigation | **Single-Model screen-swap**: `Screen` DU + `History: Screen list` in one root Model; no NavigationPage/Shell. Android hardware-back and visible Back buttons dispatch `GoBack` |
| Customer location | **Fixed seed location** by default (from the logged-in customer's seed row); DevSettings can override (see below) |
| Home screen | Greeting + "Book a New Service" CTA + **list of this customer's non-terminal jobs** (tap → Tracking/Chat for that job) |
| Auto-Reply chat toggle | **Deferred to Plan 3** (Provider.Mobile) |
| Typing/Seen indicators | **Deferred to Plan 3** (needs client→server signal + a second party) |
| Developer Mode | **Minimal**: Real GPS / Simulated GPS toggle only. No route slider (that moves the *provider* — Plan 3) |
| Role picker | **None** — this app is the Customer role; Login goes straight to the 5 named customers |

## 1. Project structure

```
src/Customer.Mobile/            Fabulous MAUI app (F#)
  Domain.fs                     Screen DU, Model, Msg, Session — pure types
  Api.fs                        ApiClient: typed HttpClient over Shared DTOs, Result-wrapped
  Hub.fs                        HubClient: SignalR connection + subscription plumbing
  Location.fs                   LocationProvider: real GPS (MAUI Geolocation) / simulated
  Update.fs                     init + update : pure, MAUI-free (references Fabulous core for Cmd)
  Views/…                       One file per screen, Fabulous view DSL
  MauiProgram.fs                Host wiring
tests/Customer.Mobile.Tests/    xUnit over Update.fs — pure, no MAUI host
```

`Update.fs` (and everything it references: `Domain.fs`, `Api.fs` signatures) must not depend on `Fabulous.MauiControls` — only `Fabulous` core + `Shared` — so tests run headless. If package factoring makes this split impractical, the fallback is extracting pure state-transition functions that `update` delegates to, and testing those.

## 2. Screens (12)

| # | Screen | Contents / behavior |
| --- | --- | --- |
| 1 | Splash | Logo + tagline; auto-advance to Login after ~1.5s |
| 2 | Login | The 5 named customers (John, Mary, Steve, Susan, Bob) as tappable cards → `POST /login {Role="Customer"}`; stores `Session` |
| 3 | Home | "Hi, {name}"; **Book a New Service** CTA → Catalog; list of this customer's non-terminal jobs (`GET /jobs?customerId=`, filtered client-side to exclude Closed/Cancelled); tap job → Tracking; gear icon → DevSettings |
| 4 | Catalog | 7 service tiles (from `GET /services`) → Provider List |
| 5 | Provider List | `GET /providers?serviceId=&lat=&lng=` (pre-sorted by backend haversine); cards show name, rating ★ + count, distance (client-computed), online dot, vehicle → Provider Profile |
| 6 | Provider Profile | Photo placeholder, rating, vehicle, service; recent ratings (`GET /ratings?providerId=`); **Book** → Booking |
| 7 | Booking | Schedule choice: Now / 30 minutes / Tomorrow / Saturday; confirm → `POST /jobs` (lat/lng/address from `Model.MyLocation`) → Tracking |
| 8 | Tracking | WebView + Leaflet map (customer pin + provider car marker); marker animates on SignalR `LocationUpdated` (interpolate ~1s); status banner from job state (`JobUpdated`); distance + naive ETA (distance ÷ 40 km/h); provider card (name, vehicle, rating); **Call** (fake) and **Chat** buttons; **Cancel Job** → `PUT /jobs/{id}/cancel`; when state hits `Completed` → auto-advance to Payment |
| 9 | Chat | Message list (`GET /messages?jobId=`), live append via `MessageReceived`; text input + send (`POST /messages`); camera/gallery photo (MAUI MediaPicker, ≤5 per job, sent as base64 thumbnail ≤~100KB); no Typing/Seen |
| 10 | Payment | Phase 1 "Payment Authorized" (on entry) → 2s loading → `POST /payment/simulate` → "Transferred to Provider ${amount}" + receipt card animation → **Rate your experience** → Rating |
| 11 | Rating | 5 tappable stars + optional comment → `POST /ratings` (backend auto-closes the job) → thank-you → Home (job list refreshes; job now Closed and gone) |
| 12 | DevSettings | Toggle: **Real GPS** (MAUI Geolocation; on denial/failure falls back to seed location with a notice) / **Simulated GPS** (default; seed location, overridable by tapping a mini-map or picking Toronto / Mississauga / Brampton). Result → `Model.MyLocation` |

**Fake call** (from Tracking): modal "Calling {provider}…" → 10s timer → "Call Ended". Pure UI, `Cmd.OfTask` timer.

**Notifications:** SignalR `Notification` events (e.g. "Provider Accepted", "Payment Complete") render as a transient in-app toast/banner on whatever screen is active.

## 3. State & data flow

```fsharp
type Session = { Token: string; UserId: int; DisplayName: string }
type Screen =
    | Splash | Login | Home | Catalog
    | ProviderList of serviceId: int
    | ProviderProfile of providerId: int
    | Booking of providerId: int * serviceId: int
    | Tracking of jobId: int
    | Chat of jobId: int
    | Payment of jobId: int
    | Rating of jobId: int
    | DevSettings

type Model =
    { Screen: Screen
      History: Screen list          // GoBack pops; empty = exit/Home
      Session: Session option
      MyLocation: float * float     // resolved by Location.fs
      Services: ServiceDto list
      Providers: ProviderDto list   // current list screen's data
      Jobs: JobDto list             // my jobs (Home + Tracking source of truth)
      Messages: MessageDto list     // current chat's messages
      ProviderPositions: Map<int, float * float>  // live marker positions
      Toast: string option
      Error: string option }
```

- **Navigation:** `Navigate of Screen` pushes current screen to `History`; `GoBack` pops. Root view pattern-matches `Model.Screen`.
- **API:** every call is `Cmd.OfTask.either apiCall args OkMsg (fun ex -> ApiError ex.Message)`; envelope failures (`Success=false`) also map to `ApiError`. `ApiError` sets `Model.Error` → dismissible banner. No crashes on 404/409.
- **SignalR:** one `HubClient` connected after login, exposed as a `Program.withSubscription`; dispatches `HubJobUpdated of JobDto`, `HubMessageReceived of MessageDto`, `HubLocationUpdated of LocationDto`, `HubNotification of string`. `update` patches `Jobs` / `Messages` / `ProviderPositions` / `Toast` — all screens react to the same subscription.
- **Map:** the Tracking WebView hosts a local HTML asset (Leaflet from CDN, same as `/dev` console); F# → JS via `EvaluateJavaScriptAsync("updateProvider(lat,lng)")`, marker interpolates client-side.
- **Backend base URL:** configurable constant; Android emulator uses `http://10.0.2.2:5000`, everything else `http://localhost:5000`. Android manifest allows cleartext for the demo.

## 4. Error handling

- All HTTP through one `ApiClient` returning `Result<'t, string>`; `Error` → banner, never an unhandled exception.
- SignalR disconnects: auto-reconnect (built-in retry); banner "Reconnecting…" while down; on reconnect, refetch the active screen's data (`Jobs`/`Messages`).
- GPS permission denied in Real mode: fall back to seed location + notice in DevSettings.
- Photo capture failures (no camera on Catalyst/simulator): gallery fallback; if both unavailable, hide the button.

## 5. Testing posture

- **Heavy — `Update.fs`:** xUnit in `tests/Customer.Mobile.Tests` (plain net10.0 test project, no MAUI workload needed). Cover: navigation push/pop invariants, session set on login, each Hub message's Model patch (job upsert, message append for the active chat only, position map update), payment phase progression, rating flow returning Home with history cleared, error banner set/dismiss.
- **Light — views:** manual verification of the full happy path on the **Mac Catalyst** build (no emulator required), driven against the running Backend.Api with the `/dev` console as the provider side (accept/enroute/arrive/complete via its buttons, Start Demo for the full scripted run).
- Same deliberate deviation from the 80% rule as Plan 1: coverage concentrates on pure logic; UI is demo-verified.

## 6. Out of scope (this plan)

Provider.Mobile (Plan 3); Auto-Reply; Typing/Seen; route slider / provider movement simulation; push notifications; real payments; any Backend.Api change. If Plan 3 needs backend additions (Typing/Seen endpoints), they're specced there.

## 7. Environment notes (feed into the plan)

- **MAUI workload is not installed** in this environment (`dotnet workload list` is empty); Xcode is present at `/Applications/Xcode.app`. Plan Task 1 = `dotnet workload install maui` (+ template pack `Fabulous.MauiControls.Templates`), then verify `net10.0-*` mobile TFMs resolve; if not, apply the TFM fallback above.
- Verification platform for this machine: **Mac Catalyst** (`dotnet build -f net10.0-maccatalyst` + run the .app). Android/iOS/Windows builds are expected to work but are not gated on this machine.
