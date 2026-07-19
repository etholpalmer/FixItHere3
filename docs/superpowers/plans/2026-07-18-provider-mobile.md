# FixItHere.Demo — Plan 3: Provider.Mobile + Demo Polish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the provider-side Fabulous MAUI app (online → accept → drive → chat → complete → paid → rate customer), the two sanctioned backend amendments (online toggle endpoint, Typing/Seen hub relay), the ClientShared extraction, and the Customer.Mobile polish additions (typing/seen, in-app Start Demo) — completing the prototype.

**Architecture:** Provider.Mobile mirrors Customer.Mobile exactly (single-Model screen-swap MVU, `ProviderApiDeps` seam, linked-source headless tests, page-owned SignalR map). Cross-app plumbing is extracted to `src/Customer.Mobile/ClientShared/*.fs` (namespace `FixItHere.ClientShared`) and linked into both apps and both test projects — no fifth project.

**Tech Stack:** identical to Plan 2 (Fabulous.MauiControls, SignalR client, Leaflet WebView, xUnit).

**Spec:** `docs/superpowers/specs/2026-07-18-provider-mobile-design.md`
**Prereq:** Plan 2 executed (Customer.Mobile exists and passes its tests). If Plan 2 is not yet merged, STOP.

## Global Constraints

- Backend amendments are limited to **exactly**: `PUT /providers/{id}/online` (+ `ProviderUpdated` hub event) and `DemoHub.SendTyping`/`SendSeen` relay methods. `src/Shared` stays completely frozen — any new request DTO lives in Backend.Api.
- Projects: `src/Provider.Mobile`, `tests/Provider.Mobile.Tests`. Reuse via linked `ClientShared` sources only; no new project.
- Named providers for login: Mike's Plumbing, Joe Electric, Rapid Tire Repair, Elite HVAC. Seeded customer John (UserId resolvable via `POST /login`) is the default Start Demo customer.
- TFMs/fallback/cleartext/base-URL rules: same as Plan 2's Global Constraints (net10.0-android/ios/maccatalyst, Windows guarded; Android emulator `http://10.0.2.2:5000`).
- Auto-Reply canned rotation, in order: `"On my way."`, `"Looks good."`, `"See you shortly."`
- Typing throttle: at most one `SendTyping` per 2 s. Typing indicator auto-hides 3 s after the last `Typing` event.
- Mac Catalyst is the verification platform; both apps must run side-by-side against the live backend for acceptance.
- Commit after every green cycle; conventional commits; **no AI attribution trailers**.

## Execution profile

- **Implementation:** Sonnet 5 (`claude-sonnet-5`), one fresh subagent per task, given the task text + Global Constraints + Executor notes + Reviewer checklist.
- **Review:** Opus 4.8 (`claude-opus-4-8`) per-task diff review after green; CRITICAL/HIGH blocks the next task.
- No design renegotiation. Same **scoped Fabulous exception** as Plan 2: adapt mechanical DSL/API names only to the installed package surface; keep structure, Model/Msg shapes, file layout; note every adaptation in the commit body. The existing Customer.Mobile code (post-Plan-2) is the ground-truth reference for the DSL that actually compiled — imitate it before consulting anything else.

## Executor notes (read before every task)

1. **Compile order** (Provider app): linked `ClientShared/{Config,Geo,Http,Hub,MapHtml}.fs` → `Domain.fs` → `Api.fs` → `Update.fs` → `Location.fs` → `Views/{Splash,Login,Home,JobDetail,ActiveJob,Chat,Payment,RateCustomer,DevSettings,Root}.fs` → `MauiProgram.fs`. Linked files use `<Compile Include="..\Customer.Mobile\ClientShared\Config.fs" />` etc.
2. All Plan 2 executor notes apply verbatim (F#9 attributes, linked-file testing, JSON case-insensitivity, hub event names, no F#→JS bridge, `Cmd.ofTaskMsg` adaptation rule, warnings). Read them: `docs/superpowers/plans/2026-07-18-customer-mobile.md` §Executor notes.
3. **Purity split:** `Domain.fs`, `Api.fs`, `Update.fs` (Provider) and all `ClientShared` files except `Hub.fs` are MAUI-free. `ClientShared/Hub.fs` depends only on the SignalR client package (not MAUI) so it links into test projects safely, but tests must never call `Start`.
4. **Refactor safety (Task 2):** after extracting ClientShared, ALL existing Customer.Mobile tests must pass unchanged (only `open`/qualifier updates in test files are allowed). If a Customer test needs behavioral changes, the refactor is wrong — stop.
5. **Hub sends from update are Cmds:** `deps.SendTyping`/`SendSeen` are `int -> int -> string -> unit` fire-and-forget (the third argument is the sender's role — see the identity note below); invoke them only inside a dispatch-capturing Cmd, never bare in `update`.

   > **Corrected 2026-07-19:** this plan specified `Cmd.ofEffect`, which does **not** exist in the Fabulous version this repo actually uses (2.4.0 ships `batch, map, none, ofAsyncMsg, ofAsyncMsgOption, ofAsyncResult, ofMsg, ofMsgOption, ofSub, ofTaskMsg, ofTaskResult`; `ofEffect` arrives in Fabulous 3). The requirement as written was unsatisfiable. `Cmd.ofSub` is the v2 equivalent and satisfies the intent — the effect is deferred into the Cmd rather than run inline in `update`. Plan 2's executor notes already anticipated this ("if the name differs (`Cmd.ofSub` in older lines), adapt mechanically").

6. **Actor identity is (id, role), never a bare id:** customer and provider ids are independent sequences that both start at 1, so the seeded demo pair is customer 1 and provider 1. Compare identities via `isSelf session id role`; `MessageDto`/`SendMessageRequest` and the Typing/Seen hub events all carry `SenderRole`.
6. **One Active Job assumption:** the provider's "active job" = the single job in state `EnRoute`/`Arrived`/`InProgress`, else the accepted `Scheduled` one being viewed. Helper `activeJob : Model -> JobDto option` in Provider Domain.fs is the only place this rule lives.
7. **Slider math is pure and tested:** `Slider.position : start:(float*float) -> target:(float*float) -> pct:float -> (float*float)` (linear interpolation, pct clamped 0–1) lives in Provider `Domain.fs`.
8. **Backend tests:** reuse `tests/Backend.Api.Tests` patterns (`Factory`, envelope deserialization). Do not touch existing backend tests.

## Reviewer checklist (Opus 4.8, per task)

- Backend diff never exceeds the two sanctioned amendments; `src/Shared` untouched all plan long.
- ClientShared files are consumed by link in Provider (no copies); Customer tests green after Task 2 with no behavioral test edits.
- Provider update purity: no MAUI/Fabulous.Maui opens in Domain/Api/Update; hub sends only via a dispatch-capturing Cmd (`Cmd.ofSub` in Fabulous 2.4.0 — see Executor note 5); all state transitions go through the backend endpoints (never local job-state mutation).
- Typing/Seen: throttle honored (no `SendTyping` while cooldown true); `Seen` sent only when chat for that job is the active screen.
- Auto-Reply: only replies to messages from the job's customer (never to own/auto messages — guard on `SenderId` **and** `SenderRole`; an id-only guard silently fails for the colliding demo pair), rotation order per Global Constraints, exactly one reply per incoming message, and the `AutoReply` flag is re-checked when the delayed reply fires.
- Nav invariants + envelope discipline: same rules as Plan 2's checklist.
- Every Fabulous adaptation noted in commit bodies; conventional commits; no AI attribution trailers.

## File map

```
src/Backend.Api/Endpoints.fs        +PUT /providers/{id}/online (SetOnlineRequest local DTO)
src/Backend.Api/Services.fs         +IBroadcaster.ProviderUpdated
src/Backend.Api/Hub.fs              +DemoHub.SendTyping/SendSeen; SignalRBroadcaster.ProviderUpdated
src/Customer.Mobile/ClientShared/   Config.fs, Geo.fs, Http.fs, Hub.fs, MapHtml.fs  (extracted Task 2)
src/Provider.Mobile/                Domain.fs, Api.fs, Update.fs, Location.fs, Views/*, MauiProgram.fs
tests/Provider.Mobile.Tests/        UpdateTests.fs, ApiTests.fs (linked sources, same trick as Plan 2)
src/Customer.Mobile/…               Task 11: typing/seen display + StartDemo (Domain/Update/Api/Views deltas)
```

---

### Task 1: Backend amendments — online toggle + Typing/Seen relay

**Files:**
- Modify: `src/Backend.Api/Services.fs` (IBroadcaster + NullBroadcaster), `src/Backend.Api/Hub.fs`, `src/Backend.Api/Endpoints.fs`
- Test: `tests/Backend.Api.Tests/EndpointTests.fs` (append)

**Interfaces:**
- Produces: `PUT /providers/{id}/online` body `{"online": bool}` → `Envelope<ProviderDto>`, 404 on unknown id; hub event `ProviderUpdated` (`ProviderDto`); `DemoHub` hub methods `SendTyping(jobId, senderId)` and `SendSeen(jobId, senderId)` broadcasting `Typing`/`Seen` events `(jobId, senderId)` to `Clients.Others`.

- [ ] **Step 1: Failing endpoint test** (append to `EndpointTests.fs`):

```fsharp
[<Fact>]
let ``provider online toggle flips and returns dto`` () =
    use c = client ()
    let resp = c.PutAsJsonAsync("/providers/1/online", {| online = false |}).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<ProviderDto>>().Result
    Assert.True(env.Success)
    Assert.False(env.Data.Online)
    // and it persists:
    let env2 = c.GetFromJsonAsync<Envelope<ProviderDto>>("/providers/1").Result
    Assert.False(env2.Data.Online)

[<Fact>]
let ``provider online toggle 404s on unknown id`` () =
    use c = client ()
    let resp = c.PutAsJsonAsync("/providers/9999/online", {| online = true |}).Result
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
```

Run: `dotnet test tests/Backend.Api.Tests` — Expected: the two new tests FAIL (404/405).

- [ ] **Step 2: Implement**

`Services.fs` — add to `IBroadcaster` (and a no-op to `NullBroadcaster`):

```fsharp
    abstract ProviderUpdated: ProviderDto -> Task
```

`Hub.fs` — hub methods + broadcaster member:

```fsharp
type DemoHub() =
    inherit Hub()
    member this.SendTyping(jobId: int, senderId: int) : Task =
        this.Clients.Others.SendAsync("Typing", jobId, senderId)
    member this.SendSeen(jobId: int, senderId: int) : Task =
        this.Clients.Others.SendAsync("Seen", jobId, senderId)
```

and in `SignalRBroadcaster`: `member _.ProviderUpdated dto = ctx.Clients.All.SendAsync("ProviderUpdated", dto)`.

`Endpoints.fs` — local request DTO (top of module, own line attribute per F#9):

```fsharp
[<CLIMutable>]
type SetOnlineRequest = { Online: bool }
```

and inside `mapAll`, after the `/providers/{id}` GET:

```fsharp
    app.MapPut("/providers/{id}/online",
        Func<int, SetOnlineRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
            fun id req db hub -> task {
                match db.Providers.SingleOrDefault(fun p -> p.Id = id) |> Option.ofObj with
                | None -> return err 404 (sprintf "Provider %d not found" id)
                | Some prov ->
                    let updated = { prov with Online = req.Online }
                    db.Entry(prov).CurrentValues.SetValues(updated)
                    db.SaveChanges() |> ignore
                    let dto = toProviderDto db updated
                    do! hub.ProviderUpdated dto
                    return okJson dto })) |> ignore
```

- [ ] **Step 3: Run — all backend tests PASS (old + new), commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: provider online toggle endpoint and Typing/Seen hub relay"
```

### Task 2: ClientShared extraction (refactor Customer.Mobile, tests stay green)

**Files:**
- Create: `src/Customer.Mobile/ClientShared/Config.fs`, `ClientShared/Geo.fs`, `ClientShared/Http.fs`, `ClientShared/Hub.fs`, `ClientShared/MapHtml.fs`
- Modify: `src/Customer.Mobile/Domain.fs` (remove Geo), `Api.fs` (use Http helpers), `Views/*.fs` + `MauiProgram.fs` (namespace/qualifier updates), DELETE old `src/Customer.Mobile/Hub.fs` and `Views/MapHtml.fs`, fsproj compile lists (app + test)

**Interfaces:**
- Produces (namespace `FixItHere.ClientShared`): `Config.baseUrl : string mutable`; `Geo.distanceKm : (float*float) -> (float*float) -> float`; `Http.getEnv<'t> : HttpClient -> string -> Task<Result<'t,string>>` (+ `postEnv<'req,'t>`, `putEnv<'t>` — same envelope semantics as Plan 2's Api.fs); `MapHtml.render : baseUrl:string -> jobLat:float -> jobLng:float -> providerId:int -> string`; `HubClient(baseUrl)` with `Start : onJob:(JobDto->unit) * onMessage:(MessageDto->unit) * onLocation:(LocationDto->unit) * onNotification:(string->unit) * onTyping:(int*int->unit) * onSeen:(int*int->unit) -> Task` and `SendTyping : int -> int -> unit`, `SendSeen : int -> int -> unit` (fire-and-forget `conn.InvokeAsync("SendTyping", jobId, senderId) |> ignore`, no-op if not connected).
- Customer.Mobile behavior unchanged; ALL its existing tests pass with only `open`/qualifier edits.

- [ ] **Step 1: Create the five ClientShared files.** Content = verbatim moves of the Plan-2 implementations with namespace changed to `FixItHere.ClientShared`, plus these two deltas — `MapHtml.render` takes `baseUrl` as its first parameter (was reading `Config` directly — keep reading `FixItHere.ClientShared.Config.baseUrl` as the *default* is fine, but the explicit param is the contract); `Hub.fs` becomes:

```fsharp
module FixItHere.ClientShared.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR.Client
open FixItHere.Shared.Dtos

type HubClient(baseUrl: string) =
    let conn =
        HubConnectionBuilder().WithUrl(baseUrl + "/hub").WithAutomaticReconnect().Build()

    member _.Start
        (onJob: JobDto -> unit, onMessage: MessageDto -> unit,
         onLocation: LocationDto -> unit, onNotification: string -> unit,
         onTyping: int * int -> unit, onSeen: int * int -> unit) : Task =
        conn.On<JobDto>("JobUpdated", onJob) |> ignore
        conn.On<MessageDto>("MessageReceived", onMessage) |> ignore
        conn.On<LocationDto>("LocationUpdated", onLocation) |> ignore
        conn.On<string>("Notification", onNotification) |> ignore
        conn.On<int, int>("Typing", fun j s -> onTyping (j, s)) |> ignore
        conn.On<int, int>("Seen", fun j s -> onSeen (j, s)) |> ignore
        conn.StartAsync()

    member _.SendTyping (jobId: int) (senderId: int) : unit =
        if conn.State = HubConnectionState.Connected then
            conn.InvokeAsync("SendTyping", jobId, senderId) |> ignore
    member _.SendSeen (jobId: int) (senderId: int) : unit =
        if conn.State = HubConnectionState.Connected then
            conn.InvokeAsync("SendSeen", jobId, senderId) |> ignore
```

- [ ] **Step 2: Rewire Customer.Mobile.** Delete old `Hub.fs`/`Views/MapHtml.fs`; `Domain.fs` drops `module Geo`; `Api.fs` keeps `createDepsWith` but its private helpers become calls to `Http.getEnv/postEnv/putEnv`; views/MauiProgram switch to `FixItHere.ClientShared` opens (Tracking passes `Config.baseUrl` to `MapHtml.render`); MauiProgram's hub start becomes callback-based:

```fsharp
        let hubCmd =
            Cmd.ofEffect (fun dispatch ->
                let hub = FixItHere.ClientShared.Hub.HubClient(Config.baseUrl)
                hub.Start(
                    (HubJobUpdated >> dispatch), (HubMessageReceived >> dispatch),
                    (HubLocationUpdated >> dispatch), (HubNotification >> dispatch),
                    (fun _ -> ()), (fun _ -> ()))   // typing/seen wired in Task 11
                |> ignore)
```

Fsproj compile orders: ClientShared five first (Config, Geo, Http, Hub, MapHtml), then as before. Customer test fsproj links `ClientShared/Config.fs`, `Geo.fs`, `Http.fs` before `Domain.fs` (NOT Hub/MapHtml — not needed headless; add them only if Api.fs references force it).

- [ ] **Step 3: Verify — the whole existing suite green, app builds; commit**

```bash
dotnet test
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
git add -A && git commit -m "refactor: extract ClientShared (Config/Geo/Http/Hub/MapHtml) for cross-app reuse"
```

### Task 3: Provider.Mobile scaffold

**Files:** Create `src/Provider.Mobile/` from the Fabulous template; modify its fsproj (TFMs, Shared reference, ClientShared links, SignalR package); AndroidManifest cleartext; add to solution.

Same steps as Plan 2 Task 1 (template short name, TFM block, cleartext attribute), plus:

```bash
dotnet new fabulous-mauicontrols -o src/Provider.Mobile -n Provider.Mobile
dotnet sln add src/Provider.Mobile
dotnet add src/Provider.Mobile reference src/Shared
dotnet add src/Provider.Mobile package Microsoft.AspNetCore.SignalR.Client
```

and in the fsproj, the linked ClientShared compile items FIRST:

```xml
<Compile Include="..\Customer.Mobile\ClientShared\Config.fs" />
<Compile Include="..\Customer.Mobile\ClientShared\Geo.fs" />
<Compile Include="..\Customer.Mobile\ClientShared\Http.fs" />
<Compile Include="..\Customer.Mobile\ClientShared\Hub.fs" />
<Compile Include="..\Customer.Mobile\ClientShared\MapHtml.fs" />
```

- [ ] Build `dotnet build src/Provider.Mobile -f net10.0-maccatalyst` (template app + links compile) → commit `feat: scaffold Provider.Mobile with linked ClientShared sources`.

### Task 4: Provider pure domain + headless test project

**Files:**
- Create: `src/Provider.Mobile/Domain.fs`; `tests/Provider.Mobile.Tests/{Provider.Mobile.Tests.fsproj, UpdateTests.fs}`; add to solution.

**Interfaces:**
- Produces (namespace `FixItHere.Provider`): `Session`, `Screen` DU (`Splash | Login | Home | JobDetail of int | ActiveJob of int | Chat of int | Payment of int | RateCustomer of int | DevSettings`), `Model` (+ `Model.initial`), `Msg`, `ProviderApiDeps`, `module Nav` (push/back/resetTo — same semantics as Plan 2), `activeJob : Model -> JobDto option`, `module Slider` with `position : (float*float) -> (float*float) -> float -> (float*float)`.

- [ ] **Step 1: Test project** — same shape as Plan 2 Task 2 (net10.0 xunit, refs Shared + Fabulous core, links `ClientShared/{Config,Geo,Http}.fs` then `..\..\src\Provider.Mobile\Domain.fs` (+ later `Api.fs`/`Update.fs` with `Exists` conditions), then test files).

- [ ] **Step 2: Failing tests**:

```fsharp
module FixItHere.Provider.Tests.UpdateTests

open System.Threading.Tasks
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Provider

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 4; ProviderName = "Elite HVAC"
      ServiceId = 7; ServiceName = "HVAC"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.70; Lng = -79.40; Address = "1 Demo St" }

[<Fact>]
let ``nav push and back mirror customer app`` () =
    let m = Nav.push { Model.initial with Screen = Home } DevSettings
    Assert.Equal(DevSettings, m.Screen)
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``activeJob picks the in-flight job over scheduled`` () =
    let m = { Model.initial with Jobs = [mkJob 1 "Scheduled"; mkJob 2 "EnRoute"; mkJob 3 "Closed"] }
    Assert.Equal(Some 2, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``activeJob is None when nothing in flight`` () =
    let m = { Model.initial with Jobs = [mkJob 3 "Closed"; mkJob 4 "Cancelled"] }
    Assert.Equal(None, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``slider position interpolates and clamps`` () =
    Assert.Equal((5.0, 5.0), Slider.position (0.0, 0.0) (10.0, 10.0) 0.5)
    Assert.Equal((10.0, 10.0), Slider.position (0.0, 0.0) (10.0, 10.0) 1.7)
    Assert.Equal((0.0, 0.0), Slider.position (0.0, 0.0) (10.0, 10.0) -0.3)
```

Run: `dotnet test tests/Provider.Mobile.Tests` — FAIL (namespace undefined).

- [ ] **Step 3: Implement `src/Provider.Mobile/Domain.fs`**

```fsharp
namespace FixItHere.Provider

open System.Threading.Tasks
open FixItHere.Shared.Dtos

type Session = { Token: string; UserId: int; DisplayName: string }

type Screen =
    | Splash | Login | Home
    | JobDetail of jobId: int
    | ActiveJob of jobId: int
    | Chat of jobId: int
    | Payment of jobId: int
    | RateCustomer of jobId: int
    | DevSettings

type Model =
    { Screen: Screen
      History: Screen list
      Session: Session option
      Online: bool
      MyLocation: float * float
      UseRealGps: bool
      SliderStart: (float * float) option
      Jobs: JobDto list
      Messages: MessageDto list
      CustomerTyping: bool
      CustomerSeen: bool
      TypingCooldown: bool
      AutoReply: bool
      AutoRepliesSent: int
      ChatDraft: string
      RatingStars: int
      RatingComment: string
      PaymentResult: PaymentResult option
      FakeCallActive: bool
      Toast: string option
      Error: string option }

module Model =
    let initial =
        { Screen = Splash; History = []; Session = None; Online = false
          MyLocation = (43.70, -79.45); UseRealGps = false; SliderStart = None
          Jobs = []; Messages = []
          CustomerTyping = false; CustomerSeen = false; TypingCooldown = false
          AutoReply = false; AutoRepliesSent = 0
          ChatDraft = ""; RatingStars = 5; RatingComment = ""
          PaymentResult = None; FakeCallActive = false; Toast = None; Error = None }

type Msg =
    | SplashDone
    | SelectProvider of name: string
    | LoggedIn of LoginResponse
    | Navigate of Screen
    | GoBack
    | SetOnline of bool
    | OnlineChanged of ProviderDto
    | JobsLoaded of JobDto list
    | AcceptJob of jobId: int
    | Depart of jobId: int
    | MarkArrived of jobId: int
    | BeginWork of jobId: int
    | FinishWork of jobId: int
    | JobActioned of JobDto
    | GpsTick of jobId: int
    | LocationPushed of LocationDto
    | SliderMoved of pct: float
    | MessagesLoaded of MessageDto list
    | ChatDraftChanged of string
    | TypingCooldownDone
    | SendChatMessage of jobId: int * text: string * photoBase64: string
    | PickAndSendPhoto of jobId: int
    | ChatMessageSent of MessageDto
    | AutoReplyToggled of bool
    | AutoReplyDue of jobId: int
    | PaymentDelayDone of jobId: int
    | PaymentSimulated of PaymentResult
    | StarsChanged of int
    | RatingCommentChanged of string
    | SubmitRating of jobId: int * stars: int * comment: string
    | RatingSubmitted
    | StartFakeCall
    | EndFakeCall
    | SetLocation of lat: float * lng: float
    | SetUseRealGps of bool
    | StartDemo
    | DemoStarted of JobDto
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubNotification of string
    | HubTyping of jobId: int * senderId: int
    | HubSeen of jobId: int * senderId: int
    | CustomerTypingExpired
    | DismissToast
    | DismissError
    | ApiError of string

type ProviderApiDeps =
    { Login: string -> Task<Result<LoginResponse, string>>
      SetOnline: int -> bool -> Task<Result<ProviderDto, string>>
      GetMyJobs: int -> Task<Result<JobDto list, string>>
      Accept: int -> Task<Result<JobDto, string>>
      Enroute: int -> Task<Result<JobDto, string>>
      Arrive: int -> Task<Result<JobDto, string>>
      Start: int -> Task<Result<JobDto, string>>
      Complete: int -> Task<Result<JobDto, string>>
      UpdateLocation: int -> float -> float -> Task<Result<LocationDto, string>>
      GetMessages: int -> Task<Result<MessageDto list, string>>
      SendMessage: SendMessageRequest -> Task<Result<MessageDto, string>>
      SimulatePayment: int -> Task<Result<PaymentResult, string>>
      SubmitRating: CreateRatingRequest -> Task<Result<RatingDto, string>>
      StartDemo: int -> int -> Task<Result<JobDto, string>>   // customerId, providerId
      PickPhoto: unit -> Task<Result<string, string>>
      GetGpsLocation: unit -> Task<Result<float * float, string>>
      SendTyping: int -> int -> unit
      SendSeen: int -> int -> unit }

module Nav =
    let push (m: Model) (s: Screen) = { m with Screen = s; History = m.Screen :: m.History }
    let back (m: Model) =
        match m.History with
        | prev :: rest -> { m with Screen = prev; History = rest }
        | [] -> { m with Screen = Home; History = [] }
    let resetTo (s: Screen) (m: Model) = { m with Screen = s; History = [] }

[<AutoOpen>]
module Domain =
    let private inFlight = [ "EnRoute"; "Arrived"; "InProgress" ]
    /// The single job currently being worked (spec: one Active Job at a time).
    let activeJob (m: Model) : JobDto option =
        m.Jobs |> List.tryFind (fun j -> List.contains j.State inFlight)

module Slider =
    /// Linear interpolation from start toward target; pct clamped to [0, 1].
    let position (startPos: float * float) (target: float * float) (pct: float) =
        let p = max 0.0 (min 1.0 pct)
        let (sLat, sLng), (tLat, tLng) = startPos, target
        (sLat + (tLat - sLat) * p, sLng + (tLng - sLng) * p)
```

- [ ] **Step 4: Tests PASS; app builds; commit** `feat: Provider.Mobile pure domain with activeJob and slider math`

### Task 5: Provider Api.fs

**Files:**
- Create: `src/Provider.Mobile/Api.fs` (after Domain.fs)
- Create: `tests/Provider.Mobile.Tests/ApiTests.fs` (activate Exists-conditioned include)

**Interfaces:**
- Produces: `module FixItHere.Provider.Api` with `createDepsWith : pickPhoto -> gpsLocation -> sendTyping:(int -> int -> unit) -> sendSeen:(int -> int -> unit) -> System.Net.Http.HttpMessageHandler -> string -> ProviderApiDeps` (device + hub effects injected; Api.fs stays MAUI/SignalR-free).

- [ ] **Step 1: Failing tests** — same StubHandler pattern as Plan 2's ApiTests (copy the `StubHandler` type into this file):

```fsharp
module FixItHere.Provider.Tests.ApiTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Xunit
open FixItHere.Provider

type StubHandler(status: HttpStatusCode, json: string) =
    inherit HttpMessageHandler()
    override _.SendAsync(_req, _ct) =
        let resp = new HttpResponseMessage(status)
        resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        Task.FromResult resp

let depsWith status json =
    Api.createDepsWith
        (fun () -> Task.FromResult(Error "no photo"))
        (fun () -> Task.FromResult(Ok (43.70, -79.45)))
        (fun _ _ -> ()) (fun _ _ -> ())
        (new StubHandler(status, json)) "http://stub"

[<Fact>]
let ``accept maps success envelope to Ok JobDto`` () =
    let deps = depsWith HttpStatusCode.OK
                 """{"success":true,"data":{"id":7,"customerId":1,"customerName":"John","providerId":4,"providerName":"Elite HVAC","serviceId":7,"serviceName":"HVAC","state":"Scheduled","price":85,"scheduledFor":"Now","lat":43.7,"lng":-79.4,"address":"1 Demo St"},"error":null}"""
    match (deps.Accept 7).Result with
    | Ok j -> Assert.Equal(7, j.Id)
    | Error e -> failwith e

[<Fact>]
let ``invalid transition envelope maps to Error`` () =
    let deps = depsWith HttpStatusCode.Conflict
                 """{"success":false,"data":null,"error":"Invalid transition"}"""
    match (deps.Complete 7).Result with
    | Error e -> Assert.Contains("Invalid transition", e)
    | Ok _ -> failwith "expected Error"
```

Run: FAIL (`Api` undefined).

- [ ] **Step 2: Implement `src/Provider.Mobile/Api.fs`** — all HTTP via `FixItHere.ClientShared.Http`:

```fsharp
module FixItHere.Provider.Api

open System
open System.Net.Http
open System.Threading.Tasks
open FixItHere.ClientShared
open FixItHere.Shared.Dtos
open FixItHere.Provider

let createDepsWith
    (pickPhoto: unit -> Task<Result<string, string>>)
    (gpsLocation: unit -> Task<Result<float * float, string>>)
    (sendTyping: int -> int -> unit)
    (sendSeen: int -> int -> unit)
    (handler: HttpMessageHandler)
    (baseUrl: string) : ProviderApiDeps =
    let http = new HttpClient(handler, BaseAddress = Uri(baseUrl))
    let transition (path: string) (jobId: int) : Task<Result<JobDto, string>> =
        Http.putEnv http (sprintf "/jobs/%d/%s" jobId path)
    { Login = fun name -> Http.postEnv http "/login" { Role = "Provider"; Name = name }
      SetOnline = fun id online ->
          Http.putBodyEnv http (sprintf "/providers/%d/online" id) {| online = online |}
      GetMyJobs = fun providerId -> Http.getEnv http (sprintf "/jobs?providerId=%d" providerId)
      Accept = transition "accept"
      Enroute = transition "enroute"
      Arrive = transition "arrive"
      Start = transition "start"
      Complete = transition "complete"
      UpdateLocation = fun id lat lng ->
          Http.putBodyEnv http "/location" { ProviderId = id; Lat = lat; Lng = lng }
      GetMessages = fun jobId -> Http.getEnv http (sprintf "/messages?jobId=%d" jobId)
      SendMessage = fun req -> Http.postEnv http "/messages" req
      SimulatePayment = fun jobId -> Http.postEnv http "/payment/simulate" { JobId = jobId }
      SubmitRating = fun req -> Http.postEnv http "/ratings" req
      StartDemo = fun customerId providerId ->
          Http.postEnv http "/dev/demo/start" {| customerId = customerId; providerId = providerId |}
      PickPhoto = pickPhoto
      GetGpsLocation = gpsLocation
      SendTyping = sendTyping
      SendSeen = sendSeen }
```

**Contract addition to ClientShared (do it in this task):** `Http.putBodyEnv<'req, 't> : HttpClient -> string -> 'req -> Task<Result<'t, string>>` (PUT with JSON body — `http.PutAsJsonAsync(path, body, jsonOpts)` + `readEnv`), needed by `SetOnline`/`UpdateLocation` here; Customer's Api keeps using the body-less `putEnv`. Add it to `ClientShared/Http.fs` alongside the existing helpers.

- [ ] **Step 3: Tests PASS; Provider app + Customer suite still build/pass; commit** `feat: Provider.Mobile ApiClient with injected device and hub effects`

### Task 6: Provider Update part 1 — login, online toggle, jobs, accept

**Files:**
- Create: `src/Provider.Mobile/Update.fs` (after Api.fs)
- Modify: `tests/Provider.Mobile.Tests/UpdateTests.fs` (append; add a full `stubDeps : ProviderApiDeps` with every field `Task.FromResult`-stubbed, mirroring Plan 2 Task 4's pattern, plus `SendTyping = fun _ _ -> ()` and `SendSeen = fun _ _ -> ()`)

**Interfaces:**
- Produces: `Update.init : unit -> Model * Cmd<Msg>` (initial + 1.5s `SplashDone`), `Update.update : ProviderApiDeps -> Msg -> Model -> Model * Cmd<Msg>`; helpers `apiCmd`/`delayCmd` identical in shape to Plan 2 Task 4's.

- [ ] **Step 1: Failing tests** (append):

```fsharp
let stubDeps : ProviderApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" })
      SetOnline = fun _ b -> Task.FromResult(Error "unused")
      GetMyJobs = fun _ -> Task.FromResult(Ok [])
      Accept = fun _ -> Task.FromResult(Error "unused")
      Enroute = fun _ -> Task.FromResult(Error "unused")
      Arrive = fun _ -> Task.FromResult(Error "unused")
      Start = fun _ -> Task.FromResult(Error "unused")
      Complete = fun _ -> Task.FromResult(Error "unused")
      UpdateLocation = fun _ _ _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      StartDemo = fun _ _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.70, -79.45))
      SendTyping = fun _ _ -> ()
      SendSeen = fun _ _ -> () }

let up msg model = Update.update stubDeps msg model |> fst

[<Fact>]
let ``login lands Home with session`` () =
    let resp = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Equal(Some 4, m.Session |> Option.map (fun s -> s.UserId))

[<Fact>]
let ``online changed updates flag and toast`` () =
    let dto : ProviderDto =
        { Id = 4; BusinessName = "Elite HVAC"; ServiceId = 7; ServiceName = "HVAC"
          Rating = 4.5; RatingCount = 3; Lat = 43.7; Lng = -79.4
          Online = true; Vehicle = "Box truck"; PhotoUrl = "" }
    let m = up (OnlineChanged dto) Model.initial
    Assert.True(m.Online)

[<Fact>]
let ``job actioned upserts and navigates to ActiveJob on accept from JobDetail`` () =
    let m0 = { Model.initial with Screen = JobDetail 7; Jobs = [mkJob 7 "Scheduled"] }
    let m = up (JobActioned (mkJob 7 "Scheduled")) m0
    Assert.Equal(ActiveJob 7, m.Screen)

[<Fact>]
let ``job actioned elsewhere just upserts`` () =
    let m0 = { Model.initial with Screen = ActiveJob 7; Jobs = [mkJob 7 "EnRoute"] }
    let m = up (JobActioned (mkJob 7 "Arrived")) m0
    Assert.Equal(ActiveJob 7, m.Screen)
    Assert.Equal("Arrived", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)
```

Run: FAIL.

- [ ] **Step 2: Implement Update.fs part 1** — same skeleton as Plan 2 Task 4 (apiCmd/delayCmd/init verbatim pattern):

```fsharp
module FixItHere.Provider.Update

open System.Threading.Tasks
open Fabulous
open FixItHere.Shared.Dtos
open FixItHere.Provider

let apiCmd (work: unit -> Task<Result<'a, string>>) (ok: 'a -> Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        try
            match! work () with
            | Ok v -> return ok v
            | Error e -> return ApiError e
        with ex -> return ApiError ex.Message
    })

let delayCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task { do! Task.Delay ms
                          return msg })

let init () = Model.initial, delayCmd 1500 SplashDone

let update (deps: ProviderApiDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SplashDone -> { model with Screen = Login; History = [] }, Cmd.none
    | SelectProvider name -> model, apiCmd (fun () -> deps.Login name) LoggedIn
    | LoggedIn resp ->
        let session = { Token = resp.Token; UserId = resp.UserId; DisplayName = resp.DisplayName }
        Nav.resetTo Home { model with Session = Some session },
        apiCmd (fun () -> deps.GetMyJobs resp.UserId) JobsLoaded
    | Navigate target ->
        let m = Nav.push model target
        let cmd =
            match target, model.Session with
            | Home, Some s -> apiCmd (fun () -> deps.GetMyJobs s.UserId) JobsLoaded
            | Chat jobId, _ -> apiCmd (fun () -> deps.GetMessages jobId) MessagesLoaded
            | Payment jobId, _ -> delayCmd 2000 (PaymentDelayDone jobId)
            | _ -> Cmd.none
        m, cmd
    | GoBack -> Nav.back model, Cmd.none
    | SetOnline b ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some s -> model, apiCmd (fun () -> deps.SetOnline s.UserId b) OnlineChanged
    | OnlineChanged dto ->
        { model with Online = dto.Online
                     Toast = Some (if dto.Online then "You are Online" else "You are Offline") },
        Cmd.none
    | JobsLoaded xs -> { model with Jobs = xs }, Cmd.none
    | AcceptJob id -> model, apiCmd (fun () -> deps.Accept id) JobActioned
    | Depart id -> model, apiCmd (fun () -> deps.Enroute id) JobActioned
    | MarkArrived id -> model, apiCmd (fun () -> deps.Arrive id) JobActioned
    | BeginWork id -> model, apiCmd (fun () -> deps.Start id) JobActioned
    | FinishWork id -> model, apiCmd (fun () -> deps.Complete id) JobActioned
    | JobActioned job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        let m = { model with Jobs = jobs }
        match model.Screen, job.State with
        | JobDetail id, _ when id = job.Id -> Nav.push m (ActiveJob job.Id), Cmd.none
        | ActiveJob id, "EnRoute" when id = job.Id && m.UseRealGps ->
            m, delayCmd 3000 (GpsTick job.Id)          // start GPS streaming loop
        | ActiveJob id, "Completed" when id = job.Id ->
            Nav.push m (Payment job.Id), delayCmd 2000 (PaymentDelayDone job.Id)
        | _ -> m, Cmd.none
    | ApiError e -> { model with Error = Some e }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | DismissToast -> { model with Toast = None }, Cmd.none
    // Part-2 arms (Task 7) — inert until then:
    | GpsTick _ | LocationPushed _ | SliderMoved _ | MessagesLoaded _
    | ChatDraftChanged _ | TypingCooldownDone | SendChatMessage _ | PickAndSendPhoto _
    | ChatMessageSent _ | AutoReplyToggled _ | AutoReplyDue _
    | PaymentDelayDone _ | PaymentSimulated _ | StarsChanged _ | RatingCommentChanged _
    | SubmitRating _ | RatingSubmitted | StartFakeCall | EndFakeCall
    | SetLocation _ | SetUseRealGps _ | StartDemo | DemoStarted _
    | HubJobUpdated _ | HubMessageReceived _ | HubLocationUpdated _ | HubNotification _
    | HubTyping _ | HubSeen _ | CustomerTypingExpired ->
        model, Cmd.none
```

- [ ] **Step 3: Tests PASS; commit** `feat: Provider.Mobile update part 1 (login/online/jobs/accept)`

### Task 7: Provider Update part 2 — GPS/slider, chat + auto-reply + typing/seen, payment, rating, demo

**Files:**
- Modify: `src/Provider.Mobile/Update.fs` (replace inert arms — delete the catch-all group so the match stays wildcard-free)
- Modify: `tests/Provider.Mobile.Tests/UpdateTests.fs` (append)

- [ ] **Step 1: Failing tests** (append):

```fsharp
let mkChatMsg id jobId senderId : MessageDto =
    { Id = id; JobId = jobId; SenderId = senderId; SenderName = "John"
      Text = "hi"; PhotoBase64 = null; SentAt = ""; Seen = false }

let loggedIn m =
    { m with Model.Session = Some { Token = "t"; UserId = 4; DisplayName = "Elite HVAC" } }

[<Fact>]
let ``slider move sets slider start once and keeps it`` () =
    let m0 = loggedIn { Model.initial with Jobs = [mkJob 7 "EnRoute"]; MyLocation = (43.0, -79.0) }
    let m1 = up (SliderMoved 0.5) m0
    Assert.Equal(Some (43.0, -79.0), m1.SliderStart)
    let m2 = up (SliderMoved 0.9) { m1 with MyLocation = (43.4, -79.2) }
    Assert.Equal(Some (43.0, -79.0), m2.SliderStart)   // start captured once

[<Fact>]
let ``auto reply schedules only for customer message on my job when enabled`` () =
    let m0 = loggedIn { Model.initial with AutoReply = true; Jobs = [mkJob 7 "EnRoute"] }
    // customer message (senderId 1 = job customer) -> reply scheduled (cmd non-empty is hard to
    // assert portably; assert the counter increments when AutoReplyDue fires instead)
    let m1 = up (AutoReplyDue 7) m0
    Assert.Equal(1, m1.AutoRepliesSent)

[<Fact>]
let ``own hub-echoed message appends once and never schedules auto reply`` () =
    let m0 = loggedIn { Model.initial with AutoReply = true; Screen = Chat 7; Jobs = [mkJob 7 "EnRoute"] }
    let m1 = up (HubMessageReceived (mkChatMsg 10 7 4)) m0   // senderId 4 = me
    Assert.Equal(1, m1.Messages |> List.filter (fun x -> x.Id = 10) |> List.length)
    let m2 = up (HubMessageReceived (mkChatMsg 10 7 4)) m1   // duplicate echo
    Assert.Equal(1, m2.Messages |> List.filter (fun x -> x.Id = 10) |> List.length)
    Assert.Equal(0, m2.AutoRepliesSent)

[<Fact>]
let ``typing cooldown blocks resend until done`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    let m1 = up (ChatDraftChanged "h") m0
    Assert.True(m1.TypingCooldown)
    let m2 = up TypingCooldownDone m1
    Assert.False(m2.TypingCooldown)

[<Fact>]
let ``hub typing shows indicator for open chat only`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    Assert.True((up (HubTyping (7, 1)) m0).CustomerTyping)
    Assert.False((up (HubTyping (99, 1)) m0).CustomerTyping)
    Assert.False((up CustomerTypingExpired (up (HubTyping (7, 1)) m0)).CustomerTyping)

[<Fact>]
let ``hub seen marks customer seen`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    Assert.True((up (HubSeen (7, 1)) m0).CustomerSeen)

[<Fact>]
let ``rating submitted returns Home and resets`` () =
    let m0 = loggedIn { Model.initial with Screen = RateCustomer 7; History = [Payment 7; Home]; RatingStars = 2 }
    let m = up RatingSubmitted m0
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(5, m.RatingStars)
```

Run: FAIL on the new tests.

- [ ] **Step 2: Implement the part-2 arms** (replace the inert group; mirrors Plan 2 Task 5 for the arms shared with Customer — `MessagesLoaded`, `ChatDraftChanged` (plus typing throttle below), `SendChatMessage` (guard blank; clear draft), `PickAndSendPhoto` (5-photo cap), `ChatMessageSent` (dedupe append), `PaymentDelayDone`/`PaymentSimulated`, `StarsChanged`/`RatingCommentChanged`, `StartFakeCall`/`EndFakeCall` (10s), `SetLocation`, `SetUseRealGps` (true → GPS fetch), `HubLocationUpdated` (position not needed → ignore or keep for parity: ignore), `HubNotification` (toast), `DismissToast`/`DismissError` — copy those shapes from the Customer implementation in the repo). Provider-specific arms in full:

```fsharp
    | GpsTick jobId ->
        // stream own position while the job is EnRoute and Real GPS is on
        match activeJob model, model.Session with
        | Some j, Some s when j.Id = jobId && j.State = "EnRoute" && model.UseRealGps ->
            model,
            Cmd.batch
                [ apiCmd deps.GetGpsLocation (fun (la, ln) -> SetLocation (la, ln))
                  apiCmd (fun () ->
                      let la, ln = model.MyLocation
                      deps.UpdateLocation s.UserId la ln) LocationPushed
                  delayCmd 3000 (GpsTick jobId) ]
        | _ -> model, Cmd.none
    | LocationPushed loc -> { model with MyLocation = (loc.Lat, loc.Lng) }, Cmd.none
    | SliderMoved pct ->
        match activeJob model, model.Session with
        | Some job, Some s ->
            let start = model.SliderStart |> Option.defaultValue model.MyLocation
            let (la, ln) = Slider.position start (job.Lat, job.Lng) pct
            { model with SliderStart = Some start },
            apiCmd (fun () -> deps.UpdateLocation s.UserId la ln) LocationPushed
        | _ -> model, Cmd.none
    | AutoReplyToggled b -> { model with AutoReply = b }, Cmd.none
    | AutoReplyDue jobId ->
        let canned = [ "On my way."; "Looks good."; "See you shortly." ]
        let text = canned.[model.AutoRepliesSent % canned.Length]
        { model with AutoRepliesSent = model.AutoRepliesSent + 1 },
        Cmd.ofMsg (SendChatMessage (jobId, text, null))
    | ChatDraftChanged t ->
        let m = { model with ChatDraft = t }
        match model.Screen, model.Session with
        | Chat jobId, Some s when not model.TypingCooldown ->
            { m with TypingCooldown = true },
            Cmd.batch
                [ Cmd.ofEffect (fun _ -> deps.SendTyping jobId s.UserId)
                  delayCmd 2000 TypingCooldownDone ]
        | _ -> m, Cmd.none
    | TypingCooldownDone -> { model with TypingCooldown = false }, Cmd.none
    | HubTyping (jobId, senderId) ->
        match model.Screen, model.Session with
        | Chat id, Some s when id = jobId && senderId <> s.UserId ->
            { model with CustomerTyping = true }, delayCmd 3000 CustomerTypingExpired
        | _ -> model, Cmd.none
    | CustomerTypingExpired -> { model with CustomerTyping = false }, Cmd.none
    | HubSeen (jobId, senderId) ->
        match model.Screen, model.Session with
        | Chat id, Some s when id = jobId && senderId <> s.UserId ->
            { model with CustomerSeen = true }, Cmd.none
        | _ -> model, Cmd.none
    | HubMessageReceived m2 ->
        let me = model.Session |> Option.map (fun s -> s.UserId)
        let activeChatJob = match model.Screen with Chat id -> Some id | ActiveJob id -> Some id | _ -> None
        let isMine = me = Some m2.SenderId
        let append =
            activeChatJob = Some m2.JobId
            && not (model.Messages |> List.exists (fun x -> x.Id = m2.Id))
        let m = if append then { model with Messages = model.Messages @ [m2] } else model
        let cmds =
            [ // mark seen if I'm looking at this chat and it's not my own message
              match model.Screen, model.Session with
              | Chat id, Some s when id = m2.JobId && not isMine ->
                  Cmd.ofEffect (fun _ -> deps.SendSeen m2.JobId s.UserId)
              | _ -> Cmd.none
              // auto-reply to the customer's message on one of my jobs
              if model.AutoReply && not isMine
                 && model.Jobs |> List.exists (fun j -> j.Id = m2.JobId && j.CustomerId = m2.SenderId) then
                  delayCmd 5000 (AutoReplyDue m2.JobId)
              else Cmd.none ]
        m, Cmd.batch cmds
    | HubJobUpdated job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        { model with Jobs = jobs }, Cmd.none
    | StartDemo ->
        match model.Session with
        | Some s -> model, apiCmd (fun () -> deps.StartDemo 1 s.UserId) DemoStarted   // customer 1 = John (seed order)
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
    | DemoStarted job ->
        { model with Toast = Some (sprintf "Demo started (job #%d)" job.Id) }, Cmd.none
    | SubmitRating (jobId, stars, comment) ->
        match model.Session, model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
        | Some s, Some job ->
            let req = { JobId = jobId; RaterId = s.UserId; RateeId = job.CustomerId
                        Stars = stars; Comment = comment }
            model, apiCmd (fun () -> deps.SubmitRating req) (fun _ -> RatingSubmitted)
        | _ -> model, Cmd.ofMsg (ApiError "Job not found")
    | RatingSubmitted ->
        let refresh =
            match model.Session with
            | Some s -> apiCmd (fun () -> deps.GetMyJobs s.UserId) JobsLoaded
            | None -> Cmd.none
        Nav.resetTo Home
            { model with Toast = Some "Thanks!"; PaymentResult = None
                         RatingStars = 5; RatingComment = ""; SliderStart = None },
        refresh
```

(The remaining shared-shape arms: copy from `src/Customer.Mobile/Update.fs` — same logic, Provider types. `HubLocationUpdated _ -> model, Cmd.none` for the provider.)

- [ ] **Step 3: All Provider tests PASS; commit** `feat: Provider.Mobile update part 2 (gps/slider/chat/auto-reply/typing/payment/rating)`

### Task 8: Provider Views part 1 — Splash/Login/Home/JobDetail + Root + MauiProgram

**Files:**
- Create: `src/Provider.Mobile/Location.fs` (identical content to Customer's `Location.fs`, namespace `FixItHere.Provider.Location` — 12 lines, copy is fine here since it's MAUI-bound and tiny), `Views/{Splash,Login,Home,JobDetail,Root}.fs`
- Modify: `src/Provider.Mobile/MauiProgram.fs` (replace template), fsproj compile order; delete template sample files

**Pattern authority:** the compiled Customer.Mobile views. Same DSL, same `AnyView`-or-uniform-widget rule, same overlay Grid in Root (toast/error/fake-call), same MauiProgram shape (deps construction, `Cmd.ofEffect` hub start on `LoggedIn` with a `hubStarted` guard). Provider-specific view content:

- `Views/Login.fs` — providers list: `[ "Mike's Plumbing"; "Joe Electric"; "Rapid Tire Repair"; "Elite HVAC" ]`, buttons dispatch `SelectProvider name`.
- `Views/Home.fs`:

```fsharp
let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    (VStack(spacing = 12.) {
        Label(sprintf "%s" name).font(size = 28.)
        (HStack(spacing = 8.) {
            Label(if model.Online then "● Online" else "○ Offline").font(size = 18.)
            Button((if model.Online then "Go Offline" else "Go Online"), SetOnline (not model.Online))
        })
        match activeJob model with
        | Some j ->
            Label("Active job").font(size = 18.)
            Button(sprintf "#%d %s — %s (%s)" j.Id j.ServiceName j.CustomerName j.State,
                   Navigate (ActiveJob j.Id))
        | None -> ()
        if model.Online then
            Label("Available jobs").font(size = 18.)
            for j in model.Jobs |> List.filter (fun j -> j.State = "Scheduled") do
                Button(sprintf "#%d %s — %s @ %s" j.Id j.ServiceName j.CustomerName j.Address,
                       Navigate (JobDetail j.Id))
        else
            Label("Go Online to see available jobs")
        Button("Developer Settings", Navigate DevSettings)
    }).padding(24.)
```

- `Views/JobDetail.fs` — job fields (customer, service, address, price, `ScheduledFor`) + `Button("Accept", AcceptJob jobId)` + Back.
- `Views/Root.fs` — same structure as Customer's Root: screen match (placeholders for ActiveJob/Chat/Payment/RateCustomer/DevSettings until Tasks 9–10) + toast/error/fake-call overlays.
- `MauiProgram.fs` — as Customer's, but: deps = `Api.createDepsWith pickPhoto gpsLocation hub.SendTyping hub.SendSeen (new HttpClientHandler()) Config.baseUrl` where the `HubClient` instance is created once at module scope so its send methods can be injected; hub `Start` wiring maps the six callbacks to `HubJobUpdated`/`HubMessageReceived`/`HubLocationUpdated`/`HubNotification`/`HubTyping`/`HubSeen` dispatches.

**Note:** creating `HubClient` at module scope but calling `Start` only on first `LoggedIn` (same guard pattern) — `SendTyping`/`SendSeen` no-op until connected (ClientShared guards on connection state).

- [ ] Build + headless tests + manual check (backend running: login as Mike's Plumbing → Home shows Online toggle + seeded Scheduled jobs when Online; toggling Offline hides them and flips the dot in `/dev`'s provider popup after refresh) → commit `feat: Provider.Mobile views part 1 (login/home/accept) with app bootstrap`.

### Task 9: Provider Views part 2 — ActiveJob + Chat

**Files:**
- Create: `src/Provider.Mobile/Views/ActiveJob.fs`, `Views/Chat.fs`; update Root placeholders; fsproj order.

- `Views/ActiveJob.fs` — Grid rows [Auto; Star; Auto]: header (Back, customer name/address/price, state line); `WebView(HtmlWebViewSource(Html = MapHtml.render Config.baseUrl job.Lat job.Lng job.ProviderId)).gridRow(1)`; bottom bar: the **single state-driven action button** plus Chat/Call:

```fsharp
let private actionButton (j: FixItHere.Shared.Dtos.JobDto) =
    match j.State with
    | "Scheduled" -> Some ("Depart", Depart j.Id)
    | "EnRoute" -> Some ("Arrived", MarkArrived j.Id)
    | "Arrived" -> Some ("Start Work", BeginWork j.Id)
    | "InProgress" -> Some ("Complete", FinishWork j.Id)
    | _ -> None
```

rendered as a prominent button when `Some`, plus `Button("Chat", Navigate (Chat j.Id))` and `Button("Call", StartFakeCall)`.

- `Views/Chat.fs` — same as Customer's Chat (message list with photo markers, Entry bound to `ChatDraft`/`ChatDraftChanged`, Send, 📷 `PickAndSendPhoto`), plus provider extras: `Label("customer is typing…")` shown when `model.CustomerTyping`; a "✓✓ seen" marker on the last own message when `model.CustomerSeen`; `Switch`/toggle row for **Auto-Reply** bound to `model.AutoReply` dispatching `AutoReplyToggled`.

- [ ] Build + tests + manual: with Customer app (or `/dev` message injection) on the other side — provider sends/receives chat both ways; typing indicator appears in the other app while typing; seen tick appears after the other side opens chat; Auto-Reply ON → customer message auto-answered after ~5 s with the canned rotation → commit `feat: Provider.Mobile active job screen and chat with auto-reply/typing/seen`.

### Task 10: Provider Views part 3 — Payment, RateCustomer, DevSettings

**Files:**
- Create: `Views/Payment.fs` (mirror of Customer's — "Payment Authorized" → receipt → `Button("Rate customer", Navigate (RateCustomer jobId))`), `Views/RateCustomer.fs` (mirror of Customer's Rating view: stars via `StarsChanged`, comment via `RatingCommentChanged`, `SubmitRating`), `Views/DevSettings.fs`; final Root placeholders replaced.

- `Views/DevSettings.fs` — Customer's DevSettings (GPS mode buttons, city teleports) **plus**:

```fsharp
        Label("Move along route").font(size = 18.)
        (HStack(spacing = 6.) {
            for pct in [ 0.0; 0.25; 0.5; 0.75; 1.0 ] do
                Button(sprintf "%d%%" (int (pct * 100.0)), SliderMoved pct)
        })
        Button("▶ Start Demo (as this provider)", StartDemo)
```

(Buttons at fixed stops instead of a continuous Slider widget — deterministic for demos and avoids a drag-binding adaptation; if the installed DSL's `Slider` binds cleanly, using it with `SliderMoved` on value-change is an allowed mechanical upgrade — note it.)

- [ ] Build + tests + manual: accept a job, use the route buttons — `/dev` map and the Customer app's tracking car both move to each stop; 100% then Arrived → Start → Complete walks to Payment → receipt → RateCustomer → Home; **Start Demo** from DevSettings kicks the scripted flow → commit `feat: Provider.Mobile payment, customer rating, dev settings with route control`.

### Task 11: Customer.Mobile polish — typing/seen + Start Demo button

**Files:**
- Modify: `src/Customer.Mobile/Domain.fs` (Model + Msg additions), `Update.fs`, `Api.fs` (deps additions), `Views/Chat.fs`, `Views/DevSettings.fs`, `MauiProgram.fs` (hub callback wiring + deps)
- Modify: `tests/Customer.Mobile.Tests/UpdateTests.fs` (+ stubDeps fields)

**Additions (exact mirrors of the Provider implementations from Tasks 4–7 — the repo code is the reference):**
- `Model`: `ProviderTyping: bool`, `MessagesSeen: bool`, `TypingCooldown: bool` (initials false).
- `Msg`: `HubTyping of int * int`, `HubSeen of int * int`, `TypingExpired`, `TypingCooldownDone`, `StartDemo`.
- `ApiDeps`: `SendTyping: int -> int -> unit`, `SendSeen: int -> int -> unit`, `StartDemo: int -> int -> Task<Result<JobDto, string>>` (POST `/dev/demo/start`). Test stubs: no-ops / `Error "unused"`.
- `update` arms: same shapes as Provider's `ChatDraftChanged` throttle, `HubTyping`/`TypingExpired`, `HubSeen`, plus send-seen-on-receive inside the existing `HubMessageReceived` arm (only when that job's Chat is open), and `StartDemo` → picks `model.Providers |> List.tryFind (fun p -> p.Online)` (fallback: first provider) + own session → `deps.StartDemo`.
- `Views/Chat.fs`: "provider is typing…" label + ✓✓ marker. `Views/DevSettings.fs`: `Button("▶ Start Demo", StartDemo)`.
- `MauiProgram.fs`: replace the two `(fun _ -> ())` hub callbacks with `HubTyping`/`HubSeen` dispatches; inject `hub.SendTyping`/`hub.SendSeen`/StartDemo into deps (hub instance hoisted to module scope as in Provider).
- Tests: throttle set/reset, typing shown/hidden for open chat only, seen flag, StartDemo error when not logged in.

- [ ] All Customer tests PASS (updated stubs), build, commit `feat: Customer.Mobile typing/seen indicators and in-app Start Demo`.

### Task 12: Final acceptance — both apps end-to-end + README

- [ ] **Step 1:** `dotnet test` (all 4 test projects green) + both apps build for `net10.0-maccatalyst`.
- [ ] **Step 2: Two-app acceptance walk.** Backend + `/dev` console + **both apps** running side-by-side on Catalyst:
  1. Provider app: login Mike's Plumbing → Go Online.
  2. Customer app: login John → book Plumbing → Mike's Plumbing → Now.
  3. Provider app: job appears in Available → JobDetail → Accept → ActiveJob.
  4. Provider: Depart; DevSettings route buttons 25→50→75 — Customer's tracking car glides accordingly (and `/dev` map agrees).
  5. Chat both directions; typing indicator visible on each side while the other types; ✓✓ appears when the other side opens chat; toggle Auto-Reply ON in Provider and confirm a canned reply ~5 s after a customer message.
  6. Fake call from each side (10 s overlay).
  7. Provider: 100% → Arrived → Start Work → Complete → provider Payment receipt; Customer auto-advances to its Payment → rating; Provider rates customer → both Home, job Closed.
  8. Customer DevSettings **Start Demo** → scripted flow plays in both apps; Provider DevSettings **Start Demo** likewise.
  9. `/dev` **Reset Demo** → both apps re-login cleanly against fresh seed.
- [ ] **Step 3:** README: add "Run the Provider app (Mac Catalyst)" section mirroring the Customer one (`dotnet build -t:Run -f net10.0-maccatalyst src/Provider.Mobile`; the four provider logins; note the two in-app Start Demo buttons). Note the two Plan 3 backend endpoints in the API list if the README enumerates endpoints.
- [ ] **Step 4:** Commit `docs: Provider.Mobile run instructions; prototype acceptance complete`.

---

## Self-review notes

- **Spec coverage:** backend amendments exactly two ✔ (T1); ClientShared extraction with Customer tests green ✔ (T2); Provider scaffold/links ✔ (T3); 10 provider screens ✔ (T4 Screen DU; T8–T10 views); Online toggle drives endpoint + live dot ✔ (T1/T6/T8); accept→enroute→arrive→start→complete via state-driven button ✔ (T9 actionButton, T6 arms); real-GPS streaming loop while EnRoute ✔ (T6 JobActioned + T7 GpsTick); route control at 0/25/50/75/100% ✔ (T7 SliderMoved + T10 buttons — spec's "slider" realized as fixed stops with a sanctioned upgrade path); Auto-Reply rotation/5 s/customer-only guard ✔ (T7); Typing/Seen both apps, throttled ✔ (T7/T9/T11); provider fake payment + rate customer ✔ (T7/T10); Start Demo buttons in both apps ✔ (T10/T11); two-app acceptance walk ✔ (T12).
- **Type consistency:** `ProviderApiDeps` fields match T5's implementation and T6/T7's usage (`Accept`/`Enroute`/`Arrive`/`Start`/`Complete`/`SetOnline`/`UpdateLocation`/`StartDemo`/`SendTyping`/`SendSeen`); `Http.putBodyEnv` introduced in T5 and used only there; ClientShared `HubClient.Start` six-callback tuple matches both MauiPrograms (T8/T11); `Slider.position`/`activeJob` defined T4, used T7/T10.
- **Known honest risks:** same Fabulous-DSL variance as Plan 2 (scoped exception + commit-body notes; post-Plan-2 Customer code is the compile-truth reference); `StartDemo` hardcodes customer id 1 (John is seeded first — verified in Plan 1's seeder) for the Provider-side button, Customer-side uses its own session; hub relay (`Typing`/`Seen`) is manually verified (no automated hub-method test) — accepted for the demo, noted in the spec's testing posture.
