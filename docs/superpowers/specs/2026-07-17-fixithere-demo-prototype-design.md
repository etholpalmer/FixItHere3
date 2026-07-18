# FixItHere.Demo — Prototype Design

**Date:** 2026-07-17
**Status:** Approved (brainstorming) — ready for implementation planning
**Source docs:** [PlanTheApp.md](../../PlanTheApp.md) (domain language), [Prototype-LLM.md](../../Prototype-LLM.md) (scope-reduction instructions)

## Purpose

Build a proof-of-concept for the FixItHere mobile-services marketplace. The goal is to **prove the experience, not the business rules**: a viewer should watch a five-minute demo and understand exactly how the marketplace works. Aggressively reduced scope; every run resets to identical seed data; a backend-driven "Start Demo" plays the whole flow hands-free.

## Confirmed decisions

| Decision | Choice |
| --- | --- |
| Mobile + backend stack | **.NET MAUI + F#** throughout; single-language .NET solution |
| Backend | F# ASP.NET Core Minimal API, EF Core, SQLite, SignalR, JWT (fake) |
| MAUI UI framework | **Fabulous (MVU)** — no XAML, functional |
| Map / tracking | **WebView + Leaflet + OpenStreetMap** tiles (no API key) |
| Target platforms | Android, iOS, Windows, Mac Catalyst (one MAUI codebase per app) |
| Demo Control Panel | **Approach A** — dev-only web console served by `Backend.Api` at `/dev`, Development environment only |
| Build order | **Control-panel-first**: `Shared` → `Backend.Api` + `/dev` console → Customer app → Provider app → polish |

## 1. Solution structure (exactly 4 projects)

```
FixItHere.Demo.sln
/src
  Shared            F# library — domain types, DTOs, Job state machine (pure, zero deps)
  Backend.Api       F# ASP.NET Core Minimal API + EF Core/SQLite + SignalR + /dev console
  Customer.Mobile   F# .NET MAUI app (Fabulous)
  Provider.Mobile   F# .NET MAUI app (Fabulous)
```

No Admin Portal, websites, Event Sourcing, CQRS, message bus, Stripe, auth server, notifications, email/SMS, background workers, Kubernetes, or microservices. `Shared` is referenced by the other three.

## 2. Reduced domain model

Derived from the [PlanTheApp.md](../../PlanTheApp.md) glossary, cut to the "prove the experience" set.

**In scope:** Service, Provider, Customer, Job (`kind = service` only), Rating, Message, Location, fake Payment.

**Explicitly out of scope** (per Prototype-LLM skip list): Offerings, Estimates, Inquiries, Price Commitment negotiation, Open Requests / Request Board / Claim, on-site estimates, Travel Fee, Strikes, Disputes / Appeals, Working Hours, cancellation-tier math, multi-step estimate negotiation, identity providers, KYC, analytics, fraud detection, push notifications.

**Booking is the firm/direct path only:** customer picks a provider → books → provider accepts. No estimate flow.

### Job state machine (lives in `Shared`)

Pure function, the spine of the system:

```fsharp
transition : JobState -> JobEvent -> Result<JobState, DomainError>
```

Happy path:

```
scheduled → enRoute → arrived → inProgress → completed → closed
     └──────────────────────── cancelled  (from any pre-completed state, simplified)
```

`cancelled` is a single simplified terminal state (no time-graded penalty tiers). No `paused`, `disputed`, no-show, or estimate-revision states in the prototype.

## 3. Backend.Api

### Data & seeding
- EF Core + SQLite.
- **Every startup: drop → recreate → deterministic reseed.** Fixed data, no time-based randomness — every run is byte-identical.
- Seed contents: 20 customers, 20 providers (distributed across the 7 catalog services), 50 `completed`/`closed` jobs, 30 pending jobs, ratings, messages, seeded placeholder photos.
- Named fake-login accounts are a curated subset of the seed:
  - Customers: John, Mary, Steve, Susan, Bob
  - Providers: Mike's Plumbing, Joe Electric, Rapid Tire Repair, Elite HVAC
  - Catalog services (7): Plumbing, Electrical, Painting, Mechanic, Moving, Cleaning, **HVAC** (HVAC added so Elite HVAC has a native category).

### Services (application layer)
- `JobService` — applies the `Shared` state machine, persists, broadcasts.
- `LocationService` — stores/streams provider location.
- `ChatService` — messages + typing/seen.
- `PaymentSimulator` — fake authorize/capture/transfer.
- `DemoOrchestrator` — plays scripted "Start Demo" timelines.

### Endpoints (~18, all under a consistent `{ success, data, error }` envelope)

```
POST /login              GET /services           GET /providers        GET /providers/{id}
POST /jobs               GET /jobs               GET /jobs/{id}
PUT  /jobs/{id}/accept   PUT /jobs/{id}/enroute  PUT /jobs/{id}/arrive
PUT  /jobs/{id}/start    PUT /jobs/{id}/complete PUT /jobs/{id}/cancel
GET  /messages           POST /messages          GET /ratings          POST /ratings
GET  /location           PUT /location           POST /payment/simulate
```

Dev-only (Development environment): `POST /dev/reset`, `POST /dev/seed`, `POST /dev/demo/start`, plus the `/dev` console page and helpers for create-customer / create-provider / create-job / move-provider / inject-message / force-transition.

`/login` returns a fake JWT identifying the chosen persona; no password. Invalid state transitions return a domain error (never a 500).

### Realtime
- One SignalR hub: `DemoHub`.
- Server→client events: `JobUpdated`, `MessageReceived`, `LocationUpdated`, `Notification` (popups: "Provider Accepted", "Provider Arriving", "Payment Complete"), `Typing`, `Seen`.

### Demo Control Panel — `/dev` (Approach A)
- Static `wwwroot/dev` HTML + JS page, mapped **only in Development**.
- Calls the same REST endpoints and subscribes to `DemoHub` — the exact contract the mobile apps use.
- Capabilities: switch persona, reposition either party on the map, inject chat messages, force state transitions (accept/arrive/start/complete/cancel), simulate payment success/failure, reset the DB, create customer/provider/job, populate sample data, and **Start Demo**.
- Built **first** as the tracer bullet: once it works, the entire backend (state machine, seed, realtime) is proven and demoable with zero mobile code.

## 4. Mobile apps (Fabulous MVU)

Shared per-app services: `ApiClient` (typed `HttpClient` over `Shared` DTOs), `RealtimeClient` (SignalR), `LocationProvider` (real vs simulated), `LocalPhotoStore` (camera/gallery, ≤5 photos, never uploaded to storage).

### Customer.Mobile flow
Splash → choose role → pick customer → Login → Home → Service Catalog (Plumbing, Electrical, Painting, Mechanic, Moving, Cleaning, HVAC) → nearby providers (sorted by haversine proximity) → provider profile → **Book** (fake schedule: Now / 30 min / Tomorrow / Saturday) → live tracking → chat → provider arrives → work starts → complete → fake payment receipt → rating.

### Provider.Mobile flow
Splash → pick provider → **Online/Offline** switch (jobs appear when Online) → available jobs → accept → navigate → chat → arrived → start → complete → fake payment.

### Map & tracking (both apps)
- WebView hosting **Leaflet + OSM** tiles.
- Car marker interpolates between points every second, driven by `LocationUpdated`.
- Customer tracking view (Uber-like): provider car, ETA, distance, moving icon, provider photo, name, vehicle, rating.
- **Developer Mode:** Real GPS **or** Simulated GPS.
  - Simulated: tap-to-teleport, city search (Toronto / Mississauga / Brampton), and a 0–100% **Move Along Route** slider.

### Fake features
- **Fake calls:** "Calling Mike…" → 10s → "Call Ended". UI only, no telephony.
- **Fake payments:** "Payment Authorized" → loading → "Transferred to Provider $85.00" → animated receipt.
- **Fake notifications:** SignalR popups only (no APNS/Firebase).
- **Chat Auto-Reply toggle:** canned provider replies after 5s (for demos).
- **Chat photos:** captured locally and sent as small base64 thumbnails over `DemoHub` so the other party sees them — still not uploaded to any storage backend.

## 5. Demonstration Mode ("Start Demo")

One button (on `/dev`, optionally in-app). `DemoOrchestrator` emits a scripted timeline of state changes + location updates + chat messages over SignalR, so both apps animate the full **book → accept → move → chat → arrive → start → pay → rate** flow hands-free. Backend-driven, so it works regardless of which app windows are open. This is the primary investor artifact.

## 6. Build order (control-panel-first)

1. `Shared` — domain types, DTOs, state machine + its tests.
2. `Backend.Api` — EF/SQLite + deterministic seed → all endpoints → `DemoHub` → **`/dev` console**. System is fully drivable at the end of this step.
3. `Customer.Mobile` happy path.
4. `Provider.Mobile` happy path.
5. Polish: Simulated GPS + route slider, fake calls, Auto-Reply, `Start Demo` in-app entry points.

## 7. Data flow (representative)

```
Customer books  → POST /jobs (scheduled)
                → DemoHub: JobUpdated → Provider app shows new job
Provider accepts → PUT /jobs/{id}/accept
                → DemoHub: JobUpdated + Notification "Provider Accepted" → Customer app
Provider enRoute → PUT /jobs/{id}/enroute; LocationProvider streams PUT /location
                → DemoHub: LocationUpdated (1/sec) → Customer map animates car
arrive/start/complete → PUT transitions → JobUpdated + Notification popups
Completion      → POST /payment/simulate → animated receipt → rating screen
```

## 8. Architecture principles

- `Shared` is pure and dependency-free — the state machine is unit-testable in isolation and reused verbatim by every project (one source of truth for transitions and DTOs).
- The `/dev` console consumes the **same** REST + SignalR contract as the apps, so it is a real integration harness, not a parallel path.
- Consistent `{ success, data, error }` response envelope everywhere.
- Errors handled explicitly; invalid transitions rejected with clear domain errors.

## 9. Testing posture

- **Heavy — `Shared` state machine:** unit + property tests. Every valid path reachable; every invalid transition rejected.
- **Medium — Backend:** `WebApplicationFactory` integration tests over the endpoints + seed-determinism test (two boots produce identical data).
- **Light — MAUI UI:** validated manually via the `/dev` console and Demo Mode rather than brittle UI tests.

**Deliberate deviation:** this is below the global 80%-coverage rule for the app layer. Accepted for a throwaway prototype; coverage is concentrated where correctness matters (state machine + backend contract). Revisit if the prototype graduates toward production.

## 10. Out of scope (reaffirmed)

Everything in the Prototype-LLM "What to Skip Entirely" list, plus anything requiring real external services (Stripe, telephony, push providers, cloud storage, identity providers). No production security hardening.
