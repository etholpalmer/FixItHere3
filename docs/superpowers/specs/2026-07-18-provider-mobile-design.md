# FixItHere.Demo — Provider.Mobile + Demo Polish Design (Plan 3)

**Date:** 2026-07-18
**Status:** Approved (brainstorming, compressed round) — ready for implementation planning
**Parent specs:** [prototype design](2026-07-17-fixithere-demo-prototype-design.md), [Customer.Mobile design](2026-07-18-customer-mobile-design.md)

## Purpose

The provider-side Fabulous MAUI app (login → Online → accept → navigate → chat → arrive → start → complete → fake payment → rate customer) plus the demo-polish items deferred from Plan 2: route slider, Auto-Reply, Typing/Seen, and in-app Start Demo buttons. Completes the three-plan prototype.

## Confirmed decisions

| Decision | Choice |
| --- | --- |
| Backend amendments | **Allowed in Plan 3** (unlike Plan 2): `PUT /providers/{id}/online` + DemoHub client→server `SendTyping`/`SendSeen` relay methods. Nothing else. |
| Typing/Seen | Minimal hub relay — no persistence beyond the existing `Message.Seen` column; both apps show "typing…" and read ticks |
| Code reuse | **Linked shared files**: extract `ClientShared/` sources inside `src/Customer.Mobile` (namespace `FixItHere.ClientShared`) — Config, Geo, Http (envelope helpers), generalized HubClient (callback-based, not Msg-typed), MapHtml — linked into Provider.Mobile and the test projects. No 5th project. |
| Scope extras | In-app **Start Demo** button (DevSettings of both apps, calls `POST /dev/demo/start`) and **Provider rates Customer** screen |
| UI framework / TFMs / verification | Same as Plan 2: Fabulous.MauiControls, net10.0-* TFMs (same fallback), Mac Catalyst is the verification platform |
| App architecture | Mirror of Customer.Mobile: single-Model screen-swap MVU, `ProviderApiDeps` seam, linked-file headless tests, page-owned SignalR map |

## 1. Backend amendments (only these)

- `PUT /providers/{id}/online` with body `{ "online": bool }` → updates `Provider.Online`, returns `ProviderDto`, broadcasts new hub event **`ProviderUpdated`** (`ProviderDto`) so catalogs/dev console can react live.
- `DemoHub` gains client→server methods: `SendTyping(jobId: int, senderId: int)` → broadcasts existing `Typing` event `(jobId, senderId)` to other clients; `SendSeen(jobId: int, senderId: int)` → broadcasts existing `Seen` event `(jobId, senderId)`. Fire-and-forget, no persistence.
- Everything else in Backend.Api and Shared stays frozen.

## 2. ClientShared extraction (inside src/Customer.Mobile, linked elsewhere)

`src/Customer.Mobile/ClientShared/` — namespace `FixItHere.ClientShared`, zero MAUI dependencies except HubClient's SignalR package (already in both apps):

- `Config.fs` — mutable `baseUrl` (moves from `FixItHere.Customer.Config`)
- `Geo.fs` — `distanceKm` (moves out of Customer's Domain.fs)
- `Http.fs` — `getEnv`/`postEnv`/`putEnv` envelope helpers (extracted from Customer's Api.fs)
- `Hub.fs` — `HubClient(baseUrl)` **generalized to callbacks**: `Start(onJob, onMessage, onLocation, onNotification, onTyping, onSeen)` + send methods `SendTyping(jobId, senderId)` / `SendSeen(jobId, senderId)`; each app adapts callbacks → its own Msg dispatch
- `MapHtml.fs` — as today, plus takes an explicit `baseUrl` parameter

Customer.Mobile is refactored to consume these (its tests keep passing); Provider.Mobile links the same files.

## 3. Provider.Mobile (`src/Provider.Mobile` + `tests/Provider.Mobile.Tests`)

**Screens (10):**

1. **Splash** → auto-advance
2. **Login** — the 4 named providers (Mike's Plumbing, Joe Electric, Rapid Tire Repair, Elite HVAC); `POST /login {Role="Provider"}`
3. **Home** — **Online/Offline switch** (drives `PUT /providers/{id}/online`); when Online: list of my `Scheduled` jobs (accept candidates) + my Active Job card if any; earnings-free, ratings-free dashboard; gear → DevSettings
4. **JobDetail** — customer name, service, address, price, schedule → **Accept** (`PUT /jobs/{id}/accept`) → ActiveJob
5. **ActiveJob** — status-driven single action button (`Depart → enroute`, `Arrived → arrive`, `Start Work → start`, `Complete → complete`), map (MapHtml: customer pin + my car), Chat + fake Call buttons. While `enRoute` with **Real GPS**: a timer Cmd streams `PUT /location` every 3s. `complete` → Payment screen.
6. **Chat** — same mechanics as Customer's chat (draft in Model, photos ≤5, dedupe), plus: **Auto-Reply toggle** (ON → 5s after a customer message on my active job, POST a canned reply, rotating "On my way." / "Looks good." / "See you shortly."), typing indicator shown on `Typing` events, `SendTyping` on draft edits (throttled ~2s), `SendSeen` when the chat screen is open and a message arrives.
7. **Payment** — mirrors Customer's fake payment ("Payment Authorized" → 2s → `POST /payment/simulate` → "Transferred $X" receipt) → **Rate customer**
8. **RateCustomer** — 5 stars + comment → `POST /ratings` (RateeId = customer) → Home (backend's single-sided close already fired if customer rated first; job leaves active list either way)
9. **DevSettings** — Real/Simulated GPS toggle, city teleports, **Route Slider** (0–100%: interpolates my position from slider-start position to active job's location, `PUT /location` on each change), **Start Demo** button (`POST /dev/demo/start` with me as provider + seeded John as customer)
10. **Root** — screen switch + toast/error/fake-call overlays (same pattern as Customer)

**State/update:** `FixItHere.Provider` Domain/Update mirror Customer's structure: `ProviderApiDeps` record (login, setOnline, getMyJobs, accept/enroute/arrive/start/complete, messages, send, simulatePayment, submitRating, hub send-callbacks injected as `SendTyping`/`SendSeen` functions), pure `update`, headless tests via linked sources.

## 4. Customer.Mobile additions (small)

- Chat: show "typing…" on `Typing` events for the open job; send `SendTyping` on draft edits (same throttle) and `SendSeen` on message arrival while chat open; render a ✓✓ marker on own messages when `Seen` arrives.
- DevSettings: **Start Demo** button (uses my session's customer + the nearest online provider).
- No other Customer.Mobile changes.

## 5. Testing posture

Same split as Plan 2: heavy pure-`update` tests (both apps' test projects, linked sources — online toggle flow, accept flow, action-button state mapping, auto-reply scheduling logic, slider interpolation math, typing throttle decision, seen marking); backend amendment gets a `WebApplicationFactory` test (`PUT /providers/{id}/online` flips the dot and 404s on unknown id); hub relay + full flows verified manually on Mac Catalyst with **both apps + backend + /dev console** in the Task-final acceptance walk (customer books in Customer app → provider accepts/drives in Provider app end to end, both directions of chat with typing/seen, auto-reply demo, route slider drive, both Start Demo buttons).

## 6. Out of scope (unchanged)

Everything in the Prototype-LLM skip list; reschedule; estimates/inquiries/open requests; multi-job providers (one Active Job assumption); push notifications; production hardening. The prototype is COMPLETE after this plan.

## 7. Execution profile

Per the project's standing preference: implementation plan written for **Sonnet 5** task subagents with **Opus 4.8** per-task review — Execution profile, Executor notes, and Reviewer checklist sections mandatory, same scoped Fabulous-DSL exception as Plan 2.
