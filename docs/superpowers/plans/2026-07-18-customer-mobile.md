# FixItHere.Demo — Plan 2: Customer.Mobile (Fabulous MAUI)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the customer-side Fabulous (MVU, F#) MAUI app: login → catalog → book → live-track on a Leaflet map → chat → fake pay → rate, consuming the Plan 1 backend unchanged.

**Architecture:** Single-Model MVU with a `Screen` DU + `History` stack (no Shell/NavigationPage). All I/O behind an `ApiDeps` record of functions so `Update.fs` stays pure and headless-testable; the test project **links** the pure source files (`Domain.fs`, `Update.fs`, `Api.fs`) rather than referencing the platform-TFM app project. One SignalR connection feeds every screen through four `Hub*` messages.

**Tech Stack:** .NET 10, F#, Fabulous.MauiControls 8.x (+ Fabulous core), Microsoft.AspNetCore.SignalR.Client, WebView + Leaflet/OSM, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-18-customer-mobile-design.md`
**Backend contract (already built, do not modify):** `src/Shared/Dtos.fs`, endpoints in `src/Backend.Api/Endpoints.fs`, hub events `JobUpdated`/`MessageReceived`/`LocationUpdated`/`Notification` at `/hub`.

## Global Constraints

- **Zero Backend.Api or Shared changes.** If the app seems to need one, stop and report.
- Project: `src/Customer.Mobile` (F#, Fabulous). Tests: `tests/Customer.Mobile.Tests` (plain `net10.0` xUnit, links pure sources).
- TFMs: `net10.0-android;net10.0-ios;net10.0-maccatalyst` (+ `net10.0-windows10.0.19041.0` inside a `$([MSBuild]::IsOSPlatform('windows'))` condition). **Fallback:** if the MAUI workload cannot resolve net10 mobile TFMs, retarget Customer.Mobile to the latest TFMs the installed workload supports (e.g. net9.0-*) — Shared/Backend.Api stay net10.0; a net10-targeted app can reference a lower-TFM library, not vice versa, so if Shared cannot be referenced from a lower-TFM app project, link Shared's `Dtos.fs` source into the app instead and report the deviation.
- Verification platform on this machine: **Mac Catalyst** only (`-f net10.0-maccatalyst`). Android/iOS/Windows must compile-configure but are not build-gated here.
- Backend base URL: `http://localhost:5000` everywhere except Android emulator (`http://10.0.2.2:5000`). Android manifest allows cleartext HTTP.
- The 5 login customers: John, Mary, Steve, Susan, Bob. 7 services come from `GET /services` (never hardcode the list).
- All API responses are enveloped: `{ success: bool; data: 't; error: string }` (`FixItHere.Shared.Dtos.Envelope<'t>`, camelCase on the wire).
- Non-terminal job states: anything except `"Closed"` and `"Cancelled"`.
- Commit after every green test/build cycle; conventional commits; **no AI attribution trailers**.

## Execution profile

- **Implementation model:** Sonnet 5 (`claude-sonnet-5`) — one fresh subagent per task, given only that task's text plus the Global Constraints, Executor Notes, and Reviewer Checklist sections.
- **Review model:** Opus 4.8 (`claude-opus-4-8`) — reviews each task's diff after its build/tests pass. CRITICAL/HIGH findings block the next task.
- Task subagents must not renegotiate the design. **One scoped exception** (because Fabulous's DSL surface varies between minor versions): where a step's Fabulous-specific code doesn't compile against the installed package version, adapt *mechanical API names only* (widget constructors, modifier names, `Program`/`Cmd` function names) to what the template-generated code and the package's actual surface use — keep the MVU structure, Model/Msg shapes, and file layout exactly as written, and note every such adaptation in the commit message body.

## Executor notes (read before every task)

1. **F# compile order matters.** `.fsproj` `<Compile Include>` order for the app must be: `Config.fs` → `Domain.fs` → `Api.fs` → `Update.fs` → `Location.fs` → `Hub.fs` → `Views/MapHtml.fs` → `Views/*.fs` (Splash, Login, Home, Catalog, ProviderList, ProviderProfile, Booking, Tracking, Chat, Payment, Rating, DevSettings, Root last) → `MauiProgram.fs`. Edit the fsproj by hand when adding files.
2. **F# 9 attribute placement:** any attribute (`[<CLIMutable>]`, etc.) on a type whose body spans multiple lines goes on its own line above `type`. One-line `[<Attr>] type X = { A: int }` is only legal when the whole record is one line.
3. **The Fabulous template output is the authority for bootstrapping.** After scaffolding (Task 1), read the generated `MauiProgram.fs`/`App.fs` before writing any view code; keep its `UseFabulousApp`/builder wiring shape and adapt this plan's `MauiProgram.fs` to it (per the scoped exception above). Do not fight the template.
4. **Fabulous version pinning:** use whatever `Fabulous.MauiControls` version the official template installs; add the same-major `Fabulous` core package to the test project. If the template installs Fabulous 3.x core, `Cmd.ofEffect (fun dispatch -> ...)` exists for fire-and-forget dispatch-capturing effects; if the name differs (`Cmd.ofSub` in older lines), adapt mechanically.
5. **Pure layer must stay MAUI-free.** `Domain.fs`, `Api.fs`, `Update.fs` may open `Fabulous` (core, for `Cmd`) and `FixItHere.Shared.Dtos` — never `Fabulous.Maui`, `Microsoft.Maui.*`. The test project links these files; a MAUI `open` there breaks the headless build. `Location.fs`, `Hub.fs`, `Views/*`, `MauiProgram.fs` are the only MAUI-touching files.
6. **Linked-file testing:** `Customer.Mobile.Tests.fsproj` includes the pure sources via `<Compile Include="..\..\src\Customer.Mobile\Domain.fs" />` etc. (before its own test files) and references `src/Shared` + the `Fabulous` core package. It does NOT reference the Customer.Mobile project.
7. **`ApiDeps` is the seam.** `update` never constructs `HttpClient`; it calls `deps.GetServices ()` etc., which return `Task<Result<'t, string>>`. Tests stub `ApiDeps` with lambdas returning canned `Task.FromResult(Ok ...)`. `Api.fs` provides `ApiClient.createDeps baseUrl` for the real thing.
8. **JSON options:** deserialize with `JsonSerializerOptions(PropertyNameCaseInsensitive = true)` — the wire format is camelCase, the F# records are PascalCase.
9. **SignalR client:** package `Microsoft.AspNetCore.SignalR.Client` (latest 10.x; if unavailable, latest stable). `HubConnectionBuilder().WithUrl(baseUrl + "/hub").WithAutomaticReconnect().Build()`; `conn.On<JobDto>("JobUpdated", handler)` — the four event names are exact strings: `JobUpdated`, `MessageReceived`, `LocationUpdated`, `Notification` (payloads: `JobDto`, `MessageDto`, `LocationDto`, `string`).
10. **Tracking map has NO F#→JS bridge.** `Views/MapHtml.fs` generates a self-contained HTML string (Leaflet + SignalR from CDN) served to the WebView via `HtmlWebViewSource`; the *page itself* connects to `Config.baseUrl + "/hub"` and animates the car marker on `LocationUpdated` — exactly the proven `/dev` console pattern. F# never calls `EvaluateJavaScriptAsync`. The F#-side `ProviderPositions` map (also fed by the hub) exists only for the ETA/distance banner. Requires internet (OSM tiles + CDN), same as `/dev`.
11. **Timers without wall-clock dependencies in tests:** anything timed (splash auto-advance 1.5s, payment 2s phase, fake-call 10s) is a `Cmd` produced by `update` (e.g. `Cmd.ofTaskMsg (task { do! Task.Delay 1500 in return SplashDone })`). Tests assert on the *message handling* (`SplashDone` → navigates), never on real delays.
12. **Don't fix warnings by restructuring.** Suppress locally (`ignore`), keep the plan's shapes.
13. **Backend must be running for manual verification tasks:** `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Backend.Api --no-launch-profile` → `http://localhost:5000` (`/dev` console is the provider-side driver).

## Reviewer checklist (Opus 4.8, per task)

- Diff matches the task's Files list; no Backend.Api/Shared edits anywhere in the plan's lifetime.
- Pure layer purity: `Domain.fs`/`Api.fs`/`Update.fs` contain no `Microsoft.Maui`/`Fabulous.Maui` opens; all I/O goes through `ApiDeps`.
- Navigation invariants: every `Navigate` pushes the *current* screen to `History`; `GoBack` pops exactly one; terminal flows (Rating done) reset `History` to `[]` with `Screen = Home`.
- Hub handling: `HubJobUpdated` upserts by `Id` into `Model.Jobs`; `HubMessageReceived` appends only when it matches the active `Chat`/`Tracking` job; `HubLocationUpdated` only touches `ProviderPositions`; `HubNotification` only sets `Toast`.
- Envelope discipline: `Success=false` and transport exceptions both land in `ApiError` → `Model.Error`; no unhandled exception path from an API call.
- Every Fabulous-API adaptation (scoped exception) is noted in the commit body.
- Conventional commit, no AI attribution trailers.

## File map

```
src/Customer.Mobile/
  Customer.Mobile.fsproj      multi-TFM MAUI app, Fabulous
  Config.fs                   mutable baseUrl (MauiProgram overrides for Android)
  Domain.fs                   Session, Screen, Model, Msg, ApiDeps, Nav, Geo
  Api.fs                      Api.createDepsWith (HttpClient + envelope; device effects injected)
  Update.fs                   init, update (pure; Cmd via deps)
  Location.fs                 real GPS via MAUI Geolocation, seed fallback
  Hub.fs                      HubClient: SignalR connect + dispatch wiring
  Views/MapHtml.fs            self-contained Leaflet page (own SignalR conn animates car)
  Views/*.fs                  one file per screen + Root.fs (screen switch + overlays)
  MauiProgram.fs              builder, deps construction, hub start on login
tests/Customer.Mobile.Tests/
  Customer.Mobile.Tests.fsproj  net10.0; links Domain/Api/Update; refs Shared + Fabulous
  UpdateTests.fs                navigation, login, hub patches, payment/rating flows
  ApiTests.fs                   envelope handling via stubbed HttpMessageHandler
```

---

### Task 1: Tooling gate — MAUI workload, Fabulous template, project scaffold

**Files:**
- Create: `src/Customer.Mobile/` (from template), modify `FixItHere.Demo.sln`
- Modify: `src/Customer.Mobile/Customer.Mobile.fsproj` (TFMs, Shared reference)
- Modify: `src/Customer.Mobile/Platforms/Android/AndroidManifest.xml` (cleartext)

**Interfaces:**
- Produces: a building Fabulous MAUI app project (template counter app) targeting `net10.0-maccatalyst` (or the documented fallback TFM), referencing `src/Shared`, registered in the solution. Later tasks replace its source files.

- [ ] **Step 1: Install the MAUI workload (fail fast on tooling)**

```bash
dotnet workload install maui
dotnet workload list
```

Expected: `maui` listed. If install fails for permissions, retry with `sudo dotnet workload install maui`. If net10 manifests are unavailable, note which TFMs the workload supports — the fallback in Global Constraints applies to every later `-f` flag.

- [ ] **Step 2: Install the Fabulous template and scaffold**

```bash
dotnet new install Fabulous.MauiControls.Templates
dotnet new list fabulous
```

Use the MauiControls template's exact short name from that listing (expected: `fabulous-mauicontrols`):

```bash
dotnet new fabulous-mauicontrols -o src/Customer.Mobile -n Customer.Mobile
dotnet sln add src/Customer.Mobile
dotnet add src/Customer.Mobile reference src/Shared
```

- [ ] **Step 3: Set TFMs and Android cleartext**

In `Customer.Mobile.fsproj`, set (adapting only if Step 1 forced the fallback):

```xml
<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
<TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>
```

In `Platforms/Android/AndroidManifest.xml`, add `android:usesCleartextTraffic="true"` to the `<application>` element.

- [ ] **Step 4: Build the template app for Mac Catalyst**

```bash
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
```

Expected: Build succeeded (template counter app). **Read the generated `MauiProgram.fs`/`App.fs` now** and note the exact Fabulous bootstrap API — later tasks adapt to it (Execution profile, scoped exception).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: scaffold Customer.Mobile from Fabulous template (maui workload)"
```

### Task 2: Pure domain — Session, Screen, Model, Msg, ApiDeps, nav helpers + headless test project

**Files:**
- Create: `src/Customer.Mobile/Domain.fs` (first `<Compile>` in the app fsproj)
- Create: `tests/Customer.Mobile.Tests/Customer.Mobile.Tests.fsproj`, `tests/Customer.Mobile.Tests/UpdateTests.fs`
- Modify: `FixItHere.Demo.sln` (add test project)

**Interfaces:**
- Produces: namespace `FixItHere.Customer` — `Session`, `Screen` DU, `Model`, `Msg` DU, `ApiDeps` record (each field `... -> Task<Result<'t, string>>`), `module Nav` with `push : Model -> Screen -> Model`, `back : Model -> Model`, `resetTo : Screen -> Model -> Model`, and `Model.initial : Model`.

- [ ] **Step 1: Scaffold the test project (links pure sources; no MAUI reference)**

```bash
dotnet new xunit -lang F# -o tests/Customer.Mobile.Tests -n Customer.Mobile.Tests -f net10.0
dotnet sln add tests/Customer.Mobile.Tests
dotnet add tests/Customer.Mobile.Tests reference src/Shared
```

Then add the Fabulous **core** package at the same version the template installed (check `src/Customer.Mobile/Customer.Mobile.fsproj` for the `Fabulous.MauiControls` version; core package id is `Fabulous`):

```bash
dotnet add tests/Customer.Mobile.Tests package Fabulous
```

Replace the test fsproj's `<ItemGroup>` compile block (delete template `Tests.fs`):

```xml
<ItemGroup>
  <Compile Include="..\..\src\Customer.Mobile\Domain.fs" />
  <Compile Include="..\..\src\Customer.Mobile\Api.fs" Condition="Exists('..\..\src\Customer.Mobile\Api.fs')" />
  <Compile Include="..\..\src\Customer.Mobile\Update.fs" Condition="Exists('..\..\src\Customer.Mobile\Update.fs')" />
  <Compile Include="UpdateTests.fs" />
  <Compile Include="ApiTests.fs" Condition="Exists('ApiTests.fs')" />
</ItemGroup>
```

(The `Exists` conditions let this task build before Tasks 3–4 create those files.)

- [ ] **Step 2: Write failing tests for navigation invariants**

`tests/Customer.Mobile.Tests/UpdateTests.fs`:

```fsharp
module FixItHere.Customer.Tests.UpdateTests

open Xunit
open FixItHere.Customer

[<Fact>]
let ``push stores current screen in history`` () =
    let m = { Model.initial with Screen = Home }
    let m2 = Nav.push m Catalog
    Assert.Equal(Catalog, m2.Screen)
    Assert.Equal<Screen list>([Home], m2.History)

[<Fact>]
let ``back pops one screen`` () =
    let m = { Model.initial with Screen = Catalog; History = [Home] }
    let m2 = Nav.back m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)

[<Fact>]
let ``back on empty history lands on Home`` () =
    let m = { Model.initial with Screen = Catalog; History = [] }
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``resetTo clears history`` () =
    let m = { Model.initial with Screen = Payment 7; History = [Home; Catalog] }
    let m2 = Nav.resetTo Home m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Customer.Mobile.Tests` — Expected: FAIL (`FixItHere.Customer` undefined).

- [ ] **Step 4: Implement `src/Customer.Mobile/Domain.fs`**

```fsharp
namespace FixItHere.Customer

open System.Threading.Tasks
open FixItHere.Shared.Dtos

type Session = { Token: string; UserId: int; DisplayName: string }

type Screen =
    | Splash
    | Login
    | Home
    | Catalog
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
      History: Screen list
      Session: Session option
      MyLocation: float * float
      UseRealGps: bool
      Services: ServiceDto list
      Providers: ProviderDto list
      ProfileRatings: RatingDto list
      Jobs: JobDto list
      Messages: MessageDto list
      ProviderPositions: Map<int, float * float>
      PaymentResult: PaymentResult option
      FakeCallActive: bool
      ChatDraft: string
      RatingStars: int
      RatingComment: string
      Toast: string option
      Error: string option }

module Model =
    /// Default location = downtown Toronto; replaced by seed/GPS via SetLocation.
    let initial =
        { Screen = Splash; History = []; Session = None
          MyLocation = (43.65, -79.38); UseRealGps = false
          Services = []; Providers = []; ProfileRatings = []
          Jobs = []; Messages = []; ProviderPositions = Map.empty
          PaymentResult = None; FakeCallActive = false
          ChatDraft = ""; RatingStars = 5; RatingComment = ""
          Toast = None; Error = None }

type Msg =
    | SplashDone
    | SelectCustomer of name: string
    | LoggedIn of LoginResponse
    | Navigate of Screen
    | GoBack
    | ServicesLoaded of ServiceDto list
    | ProvidersLoaded of ProviderDto list
    | ProfileRatingsLoaded of RatingDto list
    | JobsLoaded of JobDto list
    | BookJob of providerId: int * serviceId: int * schedule: string
    | JobCreated of JobDto
    | CancelActiveJob of jobId: int
    | MessagesLoaded of MessageDto list
    | ChatDraftChanged of string
    | SendChatMessage of jobId: int * text: string * photoBase64: string
    | PickAndSendPhoto of jobId: int
    | ChatMessageSent of MessageDto
    | StarsChanged of int
    | RatingCommentChanged of string
    | PaymentDelayDone of jobId: int
    | PaymentSimulated of PaymentResult
    | SubmitRating of jobId: int * stars: int * comment: string
    | RatingSubmitted
    | StartFakeCall
    | EndFakeCall
    | SetLocation of lat: float * lng: float
    | SetUseRealGps of bool
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubNotification of string
    | DismissToast
    | DismissError
    | ApiError of string

type ApiDeps =
    { Login: string -> Task<Result<LoginResponse, string>>
      GetServices: unit -> Task<Result<ServiceDto list, string>>
      GetProviders: int -> float -> float -> Task<Result<ProviderDto list, string>>
      GetRatings: int -> Task<Result<RatingDto list, string>>
      GetJobs: int -> Task<Result<JobDto list, string>>
      CreateJob: CreateJobRequest -> Task<Result<JobDto, string>>
      CancelJob: int -> Task<Result<JobDto, string>>
      GetMessages: int -> Task<Result<MessageDto list, string>>
      SendMessage: SendMessageRequest -> Task<Result<MessageDto, string>>
      SimulatePayment: int -> Task<Result<PaymentResult, string>>
      SubmitRating: CreateRatingRequest -> Task<Result<RatingDto, string>>
      // MAUI-implemented effects, injected like the HTTP calls so update stays pure:
      PickPhoto: unit -> Task<Result<string, string>>          // base64 jpeg/png ≤ ~100KB
      GetGpsLocation: unit -> Task<Result<float * float, string>> }

module Nav =
    let push (m: Model) (s: Screen) = { m with Screen = s; History = m.Screen :: m.History }
    let back (m: Model) =
        match m.History with
        | prev :: rest -> { m with Screen = prev; History = rest }
        | [] -> { m with Screen = Home; History = [] }
    let resetTo (s: Screen) (m: Model) = { m with Screen = s; History = [] }
```

Add `<Compile Include="Domain.fs" />` as the FIRST compile item in `Customer.Mobile.fsproj` (before the template's own files for now — they'll be removed in later tasks).

- [ ] **Step 5: Run tests (PASS) + app still builds, then commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
git add -A && git commit -m "feat: Customer.Mobile pure domain (Screen/Model/Msg/ApiDeps/Nav) with headless tests"
```

### Task 3: Api.fs — HttpClient + envelope handling behind ApiDeps

**Files:**
- Create: `src/Customer.Mobile/Api.fs` (compile after Domain.fs)
- Create: `tests/Customer.Mobile.Tests/ApiTests.fs`

**Interfaces:**
- Consumes: `ApiDeps`, DTOs.
- Produces: `module FixItHere.Customer.Api` with `createDepsWith : pickPhoto: (unit -> Task<Result<string, string>>) -> gpsLocation: (unit -> Task<Result<float * float, string>>) -> System.Net.Http.HttpMessageHandler -> string -> ApiDeps`. Api.fs stays MAUI-free: the two device effects are *passed in* (MauiProgram supplies MediaPicker/Geolocation implementations in Task 7; tests supply stubs).

- [ ] **Step 1: Write failing tests with a stubbed handler**

`tests/Customer.Mobile.Tests/ApiTests.fs` (add `<Compile Include="ApiTests.fs" />` after UpdateTests.fs; the Exists-conditioned includes for Api.fs now activate):

```fsharp
module FixItHere.Customer.Tests.ApiTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Xunit
open FixItHere.Customer

type StubHandler(status: HttpStatusCode, json: string) =
    inherit HttpMessageHandler()
    override _.SendAsync(_req, _ct) =
        let resp = new HttpResponseMessage(status)
        resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        Task.FromResult resp

let stubPhoto () = Task.FromResult(Error "no photo in tests")
let stubGps () = Task.FromResult(Ok (43.65, -79.38))

let depsWith status json =
    Api.createDepsWith stubPhoto stubGps (new StubHandler(status, json)) "http://stub"

[<Fact>]
let ``success envelope maps to Ok`` () =
    let deps = depsWith HttpStatusCode.OK
                 """{"success":true,"data":[{"id":1,"name":"Plumbing"}],"error":null}"""
    match (deps.GetServices ()).Result with
    | Ok [s] -> Assert.Equal("Plumbing", s.Name)
    | other -> failwithf "unexpected: %A" other

[<Fact>]
let ``failure envelope maps to Error with message`` () =
    let deps = depsWith HttpStatusCode.Conflict
                 """{"success":false,"data":null,"error":"Invalid transition"}"""
    match (deps.CancelJob 5).Result with
    | Error e -> Assert.Contains("Invalid transition", e)
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``non-json response maps to Error not exception`` () =
    let deps = depsWith HttpStatusCode.InternalServerError "<html>boom</html>"
    match (deps.GetServices ()).Result with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error"
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Customer.Mobile.Tests` — Expected: FAIL (`Api` undefined).

- [ ] **Step 3: Implement `src/Customer.Mobile/Api.fs`**

```fsharp
module FixItHere.Customer.Api

open System
open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open System.Threading.Tasks
open FixItHere.Shared.Dtos
open FixItHere.Customer

let private jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

let private readEnv<'t> (resp: HttpResponseMessage) : Task<Result<'t, string>> =
    task {
        try
            let! env = resp.Content.ReadFromJsonAsync<Envelope<'t>>(jsonOpts)
            if env.Success then return Ok env.Data
            else return Error (if isNull env.Error then "Request failed" else env.Error)
        with ex -> return Error ex.Message
    }

let private getEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.GetAsync(path: string)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let private postEnv<'req, 't> (http: HttpClient) (path: string) (body: 'req) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PostAsJsonAsync(path, body, jsonOpts)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let private putEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PutAsync(path, null)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let createDepsWith
    (pickPhoto: unit -> Task<Result<string, string>>)
    (gpsLocation: unit -> Task<Result<float * float, string>>)
    (handler: HttpMessageHandler)
    (baseUrl: string) : ApiDeps =
    let http = new HttpClient(handler, BaseAddress = Uri(baseUrl))
    { Login = fun name -> postEnv http "/login" { Role = "Customer"; Name = name }
      GetServices = fun () -> getEnv http "/services"
      GetProviders = fun serviceId lat lng ->
          getEnv http (sprintf "/providers?serviceId=%d&lat=%f&lng=%f" serviceId lat lng)
      GetRatings = fun providerId -> getEnv http (sprintf "/ratings?providerId=%d" providerId)
      GetJobs = fun customerId -> getEnv http (sprintf "/jobs?customerId=%d" customerId)
      CreateJob = fun req -> postEnv http "/jobs" req
      CancelJob = fun jobId -> putEnv http (sprintf "/jobs/%d/cancel" jobId)
      GetMessages = fun jobId -> getEnv http (sprintf "/messages?jobId=%d" jobId)
      SendMessage = fun req -> postEnv http "/messages" req
      SimulatePayment = fun jobId -> postEnv http "/payment/simulate" { JobId = jobId }
      SubmitRating = fun req -> postEnv http "/ratings" req
      PickPhoto = pickPhoto
      GetGpsLocation = gpsLocation }
```

Add `<Compile Include="Api.fs" />` after Domain.fs in the app fsproj.

- [ ] **Step 4: Run tests (PASS) + app builds, then commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
git add -A && git commit -m "feat: Customer.Mobile ApiClient with envelope handling and stub-tested deps"
```

### Task 4: Update.fs part 1 — init, splash, login, navigation + data loading, booking

**Files:**
- Create: `src/Customer.Mobile/Update.fs` (compile after Api.fs)
- Modify: `tests/Customer.Mobile.Tests/UpdateTests.fs` (append tests)

**Interfaces:**
- Consumes: `Model`, `Msg`, `ApiDeps`, `Nav` (Task 2).
- Produces: `module FixItHere.Customer.Update` with `init : unit -> Model * Cmd<Msg>` and `update : ApiDeps -> Msg -> Model -> Model * Cmd<Msg>`. Internal helpers `apiCmd` and `delayCmd` reused by Task 5's arms.

- [ ] **Step 1: Append failing tests to `UpdateTests.fs`**

Add below the existing Nav tests (same module — add these opens at the top if missing: `System.Threading.Tasks`, `FixItHere.Shared.Dtos`):

```fsharp
let stubDeps : ApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" })
      GetServices = fun () -> Task.FromResult(Ok [])
      GetProviders = fun _ _ _ -> Task.FromResult(Ok [])
      GetRatings = fun _ -> Task.FromResult(Ok [])
      GetJobs = fun _ -> Task.FromResult(Ok [])
      CreateJob = fun _ -> Task.FromResult(Error "unused")
      CancelJob = fun _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.65, -79.38)) }

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 2; ProviderName = "Mike's Plumbing"
      ServiceId = 3; ServiceName = "Plumbing"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.65; Lng = -79.38; Address = "1 Demo St" }

let up msg model = Update.update stubDeps msg model |> fst

[<Fact>]
let ``splash advances to Login`` () =
    Assert.Equal(Login, (up SplashDone { Model.initial with Screen = Splash }).Screen)

[<Fact>]
let ``login stores session and lands on Home with empty history`` () =
    let resp = { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(Some { Token = "fake-customer-1"; UserId = 1; DisplayName = "John" }, m.Session)

[<Fact>]
let ``navigate pushes current screen`` () =
    let m = up (Navigate Catalog) { Model.initial with Screen = Home }
    Assert.Equal(Catalog, m.Screen)
    Assert.Equal<Screen list>([Home], m.History)

[<Fact>]
let ``job created goes to Tracking with job stored`` () =
    let m = up (JobCreated (mkJob 42 "Scheduled")) { Model.initial with Screen = Booking (2, 3) }
    Assert.Equal(Tracking 42, m.Screen)
    Assert.True(m.Jobs |> List.exists (fun j -> j.Id = 42))

[<Fact>]
let ``api error sets banner`` () =
    Assert.Equal(Some "boom", (up (ApiError "boom") Model.initial).Error)
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Customer.Mobile.Tests` — Expected: FAIL (`Update` undefined). (The Exists-conditioned `Update.fs` include activates once the file exists.)

- [ ] **Step 3: Implement `src/Customer.Mobile/Update.fs` (part 1 arms; part-2 arms return `model, Cmd.none` for now)**

```fsharp
module FixItHere.Customer.Update

open System.Threading.Tasks
open Fabulous
open FixItHere.Shared.Dtos
open FixItHere.Customer

/// Run an ApiDeps call; map Ok to a message, Error/exception to ApiError.
let apiCmd (work: unit -> Task<Result<'a, string>>) (ok: 'a -> Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        try
            match! work () with
            | Ok v -> return ok v
            | Error e -> return ApiError e
        with ex -> return ApiError ex.Message
    })

let delayCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        do! Task.Delay ms
        return msg
    })

let init () = Model.initial, delayCmd 1500 SplashDone

let update (deps: ApiDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SplashDone -> { model with Screen = Login; History = [] }, Cmd.none
    | SelectCustomer name -> model, apiCmd (fun () -> deps.Login name) LoggedIn
    | LoggedIn resp ->
        let session = { Token = resp.Token; UserId = resp.UserId; DisplayName = resp.DisplayName }
        Nav.resetTo Home { model with Session = Some session },
        Cmd.batch
            [ apiCmd (fun () -> deps.GetJobs resp.UserId) JobsLoaded
              apiCmd deps.GetServices ServicesLoaded ]
    | Navigate target ->
        let m = Nav.push model target
        let cmd =
            match target, model.Session with
            | Catalog, _ -> apiCmd deps.GetServices ServicesLoaded
            | ProviderList serviceId, _ ->
                let lat, lng = model.MyLocation
                apiCmd (fun () -> deps.GetProviders serviceId lat lng) ProvidersLoaded
            | ProviderProfile providerId, _ ->
                apiCmd (fun () -> deps.GetRatings providerId) ProfileRatingsLoaded
            | Home, Some s -> apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
            | Chat jobId, _ -> apiCmd (fun () -> deps.GetMessages jobId) MessagesLoaded
            | Payment jobId, _ -> delayCmd 2000 (PaymentDelayDone jobId)
            | _ -> Cmd.none
        m, cmd
    | GoBack -> Nav.back model, Cmd.none
    | ServicesLoaded xs -> { model with Services = xs }, Cmd.none
    | ProvidersLoaded xs -> { model with Providers = xs }, Cmd.none
    | ProfileRatingsLoaded xs -> { model with ProfileRatings = xs }, Cmd.none
    | JobsLoaded xs -> { model with Jobs = xs }, Cmd.none
    | BookJob (providerId, serviceId, schedule) ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some s ->
            let lat, lng = model.MyLocation
            let req =
                { CustomerId = s.UserId; ProviderId = providerId; ServiceId = serviceId
                  ScheduleChoice = schedule; Lat = lat; Lng = lng; Address = "My location" }
            model, apiCmd (fun () -> deps.CreateJob req) JobCreated
    | JobCreated job ->
        let m = { model with Jobs = job :: model.Jobs }
        Nav.push m (Tracking job.Id), Cmd.none
    | ApiError e -> { model with Error = Some e }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | DismissToast -> { model with Toast = None }, Cmd.none
    // Part-2 arms (Task 5) — inert until then:
    | CancelActiveJob _ | MessagesLoaded _ | ChatDraftChanged _ | SendChatMessage _
    | PickAndSendPhoto _ | ChatMessageSent _ | StarsChanged _ | RatingCommentChanged _
    | PaymentDelayDone _ | PaymentSimulated _ | SubmitRating _ | RatingSubmitted
    | StartFakeCall | EndFakeCall | SetLocation _ | SetUseRealGps _
    | HubJobUpdated _ | HubMessageReceived _ | HubLocationUpdated _ | HubNotification _ ->
        model, Cmd.none
```

(`Cmd.ofTaskMsg` / `Cmd.ofMsg` / `Cmd.batch` / `Cmd.none` are Fabulous core — adapt names mechanically per the scoped exception if the installed version differs.)

- [ ] **Step 4: Run tests (PASS) + app builds, then commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
git add -A && git commit -m "feat: Customer.Mobile update part 1 (splash/login/nav/booking)"
```

### Task 5: Update.fs part 2 — hub patches, chat, payment, rating, fake call, location

**Files:**
- Modify: `src/Customer.Mobile/Update.fs` (replace the inert part-2 arms)
- Modify: `tests/Customer.Mobile.Tests/UpdateTests.fs` (append tests)

**Interfaces:**
- Consumes: everything from Task 4 (`apiCmd`, `delayCmd`, part-1 arms unchanged).
- Produces: fully-implemented `update`; no signature changes.

- [ ] **Step 1: Append failing tests**

```fsharp
let mkChatMsg id jobId : MessageDto =
    { Id = id; JobId = jobId; SenderId = 2; SenderName = "Mike's Plumbing"
      Text = "On my way"; PhotoBase64 = null; SentAt = "2026-01-01T00:00:00Z"; Seen = false }

[<Fact>]
let ``hub job update upserts by id`` () =
    let m0 = { Model.initial with Jobs = [mkJob 7 "Scheduled"] }
    let m = up (HubJobUpdated (mkJob 7 "EnRoute")) m0
    Assert.Equal("EnRoute", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)
    Assert.Equal(1, List.length m.Jobs)

[<Fact>]
let ``completed job while tracking advances to Payment`` () =
    let m0 = { Model.initial with Screen = Tracking 7; Jobs = [mkJob 7 "InProgress"] }
    let m = up (HubJobUpdated (mkJob 7 "Completed")) m0
    Assert.Equal(Payment 7, m.Screen)

[<Fact>]
let ``completed job on another screen does not navigate`` () =
    let m0 = { Model.initial with Screen = Home; Jobs = [mkJob 7 "InProgress"] }
    Assert.Equal(Home, (up (HubJobUpdated (mkJob 7 "Completed")) m0).Screen)

[<Fact>]
let ``hub message appends only for the active chat job and dedupes`` () =
    let m0 = { Model.initial with Screen = Chat 7; Messages = [mkChatMsg 1 7] }
    let m1 = up (HubMessageReceived (mkChatMsg 2 7)) m0
    Assert.Equal(2, List.length m1.Messages)
    let m2 = up (HubMessageReceived (mkChatMsg 2 7)) m1      // duplicate id
    Assert.Equal(2, List.length m2.Messages)
    let m3 = up (HubMessageReceived (mkChatMsg 3 99)) m2     // other job
    Assert.Equal(2, List.length m3.Messages)

[<Fact>]
let ``hub location updates position map only`` () =
    let loc : LocationDto = { ProviderId = 2; Lat = 43.7; Lng = -79.4; UpdatedAt = "" }
    let m = up (HubLocationUpdated loc) Model.initial
    Assert.Equal((43.7, -79.4), m.ProviderPositions.[2])

[<Fact>]
let ``hub notification sets toast`` () =
    Assert.Equal(Some "Provider Accepted", (up (HubNotification "Provider Accepted") Model.initial).Toast)

[<Fact>]
let ``payment result stored`` () =
    let r : PaymentResult = { JobId = 7; Amount = 85m; Status = "Transferred" }
    Assert.Equal(Some r, (up (PaymentSimulated r) Model.initial).PaymentResult)

[<Fact>]
let ``rating submitted resets to Home and clears payment`` () =
    let m0 = { Model.initial with Screen = Rating 7; History = [Payment 7; Tracking 7; Home]
                                  PaymentResult = Some { JobId = 7; Amount = 85m; Status = "Transferred" } }
    let m = up RatingSubmitted m0
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(None, m.PaymentResult)
    Assert.True(m.Toast.IsSome)

[<Fact>]
let ``fake call toggles`` () =
    let m = up StartFakeCall Model.initial
    Assert.True(m.FakeCallActive)
    Assert.False((up EndFakeCall m).FakeCallActive)

[<Fact>]
let ``set location updates model`` () =
    Assert.Equal((43.59, -79.64), (up (SetLocation (43.59, -79.64)) Model.initial).MyLocation)

[<Fact>]
let ``sixth photo for a job is rejected with an error`` () =
    let photoMsg id : MessageDto =
        { Id = id; JobId = 7; SenderId = 1; SenderName = "John"
          Text = ""; PhotoBase64 = "ZmFrZQ=="; SentAt = ""; Seen = false }
    let m0 =
        { Model.initial with
            Session = Some { Token = "t"; UserId = 1; DisplayName = "John" }
            Screen = Chat 7
            Messages = [ for i in 1 .. 5 -> photoMsg i ] }
    let m = up (PickAndSendPhoto 7) m0
    Assert.True(m.Error.IsSome)

[<Fact>]
let ``chat draft tracks input and clears on send`` () =
    let session = Some { Token = "t"; UserId = 1; DisplayName = "John" }
    let m1 = up (ChatDraftChanged "hello") { Model.initial with Session = session }
    Assert.Equal("hello", m1.ChatDraft)
    Assert.Equal("", (up (SendChatMessage (7, "hello", null)) m1).ChatDraft)

[<Fact>]
let ``stars and comment update`` () =
    let m = up (StarsChanged 3) Model.initial
    Assert.Equal(3, m.RatingStars)
    Assert.Equal("great", (up (RatingCommentChanged "great") m).RatingComment)
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Customer.Mobile.Tests` — Expected: the new tests FAIL (part-2 arms are inert).

- [ ] **Step 3: Replace the inert arms in `Update.fs`**

```fsharp
    | CancelActiveJob jobId ->
        model, apiCmd (fun () -> deps.CancelJob jobId) HubJobUpdated
    | MessagesLoaded xs -> { model with Messages = xs }, Cmd.none
    | ChatDraftChanged t -> { model with ChatDraft = t }, Cmd.none
    | StarsChanged n -> { model with RatingStars = n }, Cmd.none
    | RatingCommentChanged t -> { model with RatingComment = t }, Cmd.none
    | SendChatMessage (jobId, text, photo) ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some _ when System.String.IsNullOrWhiteSpace text && System.String.IsNullOrEmpty photo ->
            model, Cmd.none   // nothing to send
        | Some s ->
            let req = { JobId = jobId; SenderId = s.UserId; Text = text; PhotoBase64 = photo }
            { model with ChatDraft = "" }, apiCmd (fun () -> deps.SendMessage req) ChatMessageSent
    | PickAndSendPhoto jobId ->
        // Spec: at most 5 photos per job from this customer.
        let myId = model.Session |> Option.map (fun s -> s.UserId)
        let sentPhotos =
            model.Messages
            |> List.filter (fun m ->
                m.JobId = jobId && Some m.SenderId = myId
                && not (System.String.IsNullOrEmpty m.PhotoBase64))
            |> List.length
        if sentPhotos >= 5 then
            { model with Error = Some "Photo limit reached (5 per job)" }, Cmd.none
        else
            model, apiCmd deps.PickPhoto (fun b64 -> SendChatMessage (jobId, "", b64))
    | ChatMessageSent m2 ->
        let msgs =
            if model.Messages |> List.exists (fun x -> x.Id = m2.Id)
            then model.Messages else model.Messages @ [m2]
        { model with Messages = msgs }, Cmd.none
    | PaymentDelayDone jobId ->
        model, apiCmd (fun () -> deps.SimulatePayment jobId) PaymentSimulated
    | PaymentSimulated r -> { model with PaymentResult = Some r }, Cmd.none
    | SubmitRating (jobId, stars, comment) ->
        match model.Session, model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
        | Some s, Some job ->
            let req =
                { JobId = jobId; RaterId = s.UserId; RateeId = job.ProviderId
                  Stars = stars; Comment = comment }
            model, apiCmd (fun () -> deps.SubmitRating req) (fun _ -> RatingSubmitted)
        | _ -> model, Cmd.ofMsg (ApiError "Job not found")
    | RatingSubmitted ->
        let refresh =
            match model.Session with
            | Some s -> apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
            | None -> Cmd.none
        Nav.resetTo Home
            { model with Toast = Some "Thanks for your rating!"; PaymentResult = None
                         RatingStars = 5; RatingComment = "" },
        refresh
    | StartFakeCall -> { model with FakeCallActive = true }, delayCmd 10000 EndFakeCall
    | EndFakeCall -> { model with FakeCallActive = false }, Cmd.none
    | SetLocation (lat, lng) -> { model with MyLocation = (lat, lng) }, Cmd.none
    | SetUseRealGps true ->
        { model with UseRealGps = true },
        apiCmd deps.GetGpsLocation (fun (la, ln) -> SetLocation (la, ln))
    | SetUseRealGps false -> { model with UseRealGps = false }, Cmd.none
    | HubJobUpdated job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        let m = { model with Jobs = jobs }
        match model.Screen with
        | Tracking id when id = job.Id && job.State = "Completed" ->
            Nav.push m (Payment job.Id), delayCmd 2000 (PaymentDelayDone job.Id)
        | _ -> m, Cmd.none
    | HubMessageReceived m2 ->
        let activeJob =
            match model.Screen with
            | Chat id | Tracking id -> Some id
            | _ -> None
        if activeJob = Some m2.JobId
           && not (model.Messages |> List.exists (fun x -> x.Id = m2.Id))
        then { model with Messages = model.Messages @ [m2] }, Cmd.none
        else model, Cmd.none
    | HubLocationUpdated loc ->
        { model with ProviderPositions = model.ProviderPositions.Add(loc.ProviderId, (loc.Lat, loc.Lng)) },
        Cmd.none
    | HubNotification text -> { model with Toast = Some text }, Cmd.none
```

(Delete the catch-all inert arm from Task 4 — the match must stay exhaustive with no wildcard, so the compiler flags any Msg case left unhandled.)

- [ ] **Step 4: Run tests (PASS — all Update tests) + app builds, then commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
git add -A && git commit -m "feat: Customer.Mobile update part 2 (hub/chat/payment/rating/fake-call)"
```

### Task 6: Hub.fs + Location.fs (MAUI-side services, build-gated)

**Files:**
- Create: `src/Customer.Mobile/Hub.fs`, `src/Customer.Mobile/Location.fs` (compile after Update.fs: Location.fs then Hub.fs)
- Modify: `src/Customer.Mobile/Customer.Mobile.fsproj` (add SignalR client package + compile entries)

**Interfaces:**
- Consumes: `Msg` (the four `Hub*` cases), DTOs.
- Produces: `Hub.HubClient(baseUrl: string)` with `member Start : (Msg -> unit) -> Task`; `Location.getCurrent : (float * float) -> Task<float * float>` (fallback-on-failure).

No unit tests (MAUI-touching); gate is the Catalyst build.

- [ ] **Step 1: Add the SignalR client package**

```bash
dotnet add src/Customer.Mobile package Microsoft.AspNetCore.SignalR.Client
```

- [ ] **Step 2: Implement `Location.fs`**

```fsharp
module FixItHere.Customer.Location

open System.Threading.Tasks
open Microsoft.Maui.Devices.Sensors

/// Best-effort GPS: returns fallback on permission denial, timeout, or any failure.
let getCurrent (fallback: float * float) : Task<float * float> =
    task {
        try
            let! loc = Geolocation.Default.GetLocationAsync(GeolocationRequest(GeolocationAccuracy.Medium))
            if isNull (box loc) then return fallback
            else return (loc.Latitude, loc.Longitude)
        with _ -> return fallback
    }
```

- [ ] **Step 3: Implement `Hub.fs`**

```fsharp
module FixItHere.Customer.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR.Client
open FixItHere.Shared.Dtos
open FixItHere.Customer

type HubClient(baseUrl: string) =
    let conn =
        HubConnectionBuilder()
            .WithUrl(baseUrl + "/hub")
            .WithAutomaticReconnect()
            .Build()

    /// Registers the four server events and starts the connection.
    member _.Start(dispatch: Msg -> unit) : Task =
        conn.On<JobDto>("JobUpdated", fun dto -> dispatch (HubJobUpdated dto)) |> ignore
        conn.On<MessageDto>("MessageReceived", fun dto -> dispatch (HubMessageReceived dto)) |> ignore
        conn.On<LocationDto>("LocationUpdated", fun dto -> dispatch (HubLocationUpdated dto)) |> ignore
        conn.On<string>("Notification", fun text -> dispatch (HubNotification text)) |> ignore
        conn.StartAsync()
```

- [ ] **Step 4: Build + commit**

```bash
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
dotnet test tests/Customer.Mobile.Tests
git add -A && git commit -m "feat: Customer.Mobile SignalR hub client and GPS location service"
```

### Task 7: Views part 1 — Config, Geo, Splash/Login/Home/Catalog/ProviderList/ProviderProfile, Root, MauiProgram

**Files:**
- Create: `src/Customer.Mobile/Config.fs` (FIRST compile item, before Domain.fs)
- Modify: `src/Customer.Mobile/Domain.fs` (append `module Geo`)
- Create: `src/Customer.Mobile/Views/Splash.fs`, `Views/Login.fs`, `Views/Home.fs`, `Views/Catalog.fs`, `Views/ProviderList.fs`, `Views/ProviderProfile.fs`, `Views/Root.fs`
- Modify: `src/Customer.Mobile/MauiProgram.fs` (replace template app wiring); DELETE the template's sample view/model files (e.g. `App.fs` counter sample) or fold their bootstrap into `MauiProgram.fs` per the template's shape
- Test: `tests/Customer.Mobile.Tests/UpdateTests.fs` (Geo test)

**Interfaces:**
- Consumes: everything prior. `Config.baseUrl : string` (mutable, default `http://localhost:5000`).
- Produces: `Views.Root.view : Model -> <application widget>`; each screen module exposes `view : Model -> <uniform widget>`; `MauiProgram.CreateMauiApp` runs the Fabulous program with real deps and hub wiring.

- [ ] **Step 1: Failing Geo test (pure — runs headless)**

Append to `UpdateTests.fs`:

```fsharp
[<Fact>]
let ``geo distance Toronto to Mississauga is about 21km`` () =
    let d = Geo.distanceKm (43.6532, -79.3832) (43.5890, -79.6441)
    Assert.InRange(d, 19.0, 24.0)
```

Run: `dotnet test tests/Customer.Mobile.Tests` — Expected: FAIL (`Geo` undefined).

- [ ] **Step 2: Append `module Geo` to `Domain.fs` (inside namespace, after `module Nav`)**

```fsharp
module Geo =
    let distanceKm (lat1: float, lng1: float) (lat2: float, lng2: float) =
        let rad d = d * System.Math.PI / 180.0
        let dLat = rad (lat2 - lat1)
        let dLng = rad (lng2 - lng1)
        let a =
            sin (dLat / 2.0) ** 2.0
            + cos (rad lat1) * cos (rad lat2) * sin (dLng / 2.0) ** 2.0
        6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))
```

Run the test again — PASS.

- [ ] **Step 3: Create `Config.fs` (first compile item)**

```fsharp
module FixItHere.Customer.Config

/// Backend base URL; MauiProgram overrides for Android emulator.
let mutable baseUrl = "http://localhost:5000"
```

- [ ] **Step 4: Screen views**

All screen `view` functions must return ONE uniform widget type so `Root`'s match unifies. Primary approach: wrap each match arm in `AnyView(...)`; if the installed Fabulous.MauiControls has no `AnyView`, instead make every screen view return a `ScrollView`-rooted widget (mechanical adaptation — note it in the commit body). Code below uses plausible Fabulous 8 DSL; adapt names per the scoped exception.

`Views/Splash.fs`:

```fsharp
module FixItHere.Customer.Views.Splash

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (_model: Model) =
    (VStack(spacing = 8.) {
        Label("FixItHere").font(size = 42., attributes = Microsoft.Maui.Controls.FontAttributes.Bold).centerTextHorizontal()
        Label("Mobile services, wherever you are").centerTextHorizontal()
    }).centerVertical().padding(24.)
```

`Views/Login.fs`:

```fsharp
module FixItHere.Customer.Views.Login

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let customers = [ "John"; "Mary"; "Steve"; "Susan"; "Bob" ]

let view (_model: Model) =
    (VStack(spacing = 12.) {
        Label("Who's booking today?").font(size = 24.).centerTextHorizontal()
        for name in customers do
            Button(name, SelectCustomer name)
    }).padding(24.)
```

`Views/Home.fs`:

```fsharp
module FixItHere.Customer.Views.Home

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let private nonTerminal (j: FixItHere.Shared.Dtos.JobDto) =
    j.State <> "Closed" && j.State <> "Cancelled"

let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    (VStack(spacing = 12.) {
        Label(sprintf "Hi, %s" name).font(size = 28.)
        Button("Book a New Service", Navigate Catalog)
        Label("Your active jobs").font(size = 18.)
        for j in model.Jobs |> List.filter nonTerminal do
            Button(sprintf "#%d %s — %s (%s)" j.Id j.ServiceName j.ProviderName j.State,
                   Navigate (Tracking j.Id))
        Button("Developer Settings", Navigate DevSettings)
    }).padding(24.)
```

`Views/Catalog.fs`:

```fsharp
module FixItHere.Customer.Views.Catalog

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("What do you need?").font(size = 24.)
        for s in model.Services do
            Button(s.Name, Navigate (ProviderList s.Id))
    }).padding(24.)
```

`Views/ProviderList.fs`:

```fsharp
module FixItHere.Customer.Views.ProviderList

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("Nearby providers").font(size = 24.)
        for p in model.Providers do
            let km = Geo.distanceKm model.MyLocation (p.Lat, p.Lng)
            let dot = if p.Online then "●" else "○"
            Button(sprintf "%s %s  ★%.1f (%d)  %.1f km" dot p.BusinessName p.Rating p.RatingCount km,
                   Navigate (ProviderProfile p.Id))
    }).padding(24.)
```

`Views/ProviderProfile.fs`:

```fsharp
module FixItHere.Customer.Views.ProviderProfile

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (providerId: int) =
    match model.Providers |> List.tryFind (fun p -> p.Id = providerId) with
    | None ->
        (VStack(spacing = 12.) { Button("← Back", GoBack); Label("Provider not found") }).padding(24.)
    | Some p ->
        (VStack(spacing = 12.) {
            Button("← Back", GoBack)
            Label(p.BusinessName).font(size = 28.)
            Label(sprintf "%s — %s" p.ServiceName p.Vehicle)
            Label(sprintf "★ %.1f (%d ratings)" p.Rating p.RatingCount)
            Button("Book", Navigate (Booking (p.Id, p.ServiceId)))
            Label("Recent feedback").font(size = 18.)
            for r in model.ProfileRatings |> List.truncate 5 do
                Label(sprintf "★%d  %s" r.Stars r.Comment)
        }).padding(24.)
```

`Views/Root.fs` (screens from later tasks get placeholder labels for now — replaced in Tasks 8–9):

```fsharp
module FixItHere.Customer.Views.Root

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let private placeholder (name: string) =
    (VStack(spacing = 12.) { Button("← Back", GoBack); Label(name) }).padding(24.)

let private screenView (model: Model) =
    match model.Screen with
    | Splash -> AnyView(Splash.view model)
    | Login -> AnyView(Login.view model)
    | Home -> AnyView(Home.view model)
    | Catalog -> AnyView(Catalog.view model)
    | ProviderList _ -> AnyView(ProviderList.view model)
    | ProviderProfile id -> AnyView(ProviderProfile.view model id)
    | Booking _ -> AnyView(placeholder "Booking (Task 8)")
    | Tracking _ -> AnyView(placeholder "Tracking (Task 8)")
    | Chat _ -> AnyView(placeholder "Chat (Task 8)")
    | Payment _ -> AnyView(placeholder "Payment (Task 9)")
    | Rating _ -> AnyView(placeholder "Rating (Task 9)")
    | DevSettings -> AnyView(placeholder "DevSettings (Task 9)")

let view (model: Model) =
    Application(
        ContentPage(
            (Grid(coldefs = [ Star ], rowdefs = [ Star ]) {
                screenView model
                match model.Toast with
                | Some t -> Label(t).backgroundColor(Microsoft.Maui.Graphics.Colors.DarkSlateBlue)
                                    .textColor(Microsoft.Maui.Graphics.Colors.White)
                                    .padding(12.).verticalOptions(Microsoft.Maui.Controls.LayoutOptions.Start)
                                    .onTapped(DismissToast)
                | None -> ()
                match model.Error with
                | Some e -> Label(sprintf "⚠ %s" e).backgroundColor(Microsoft.Maui.Graphics.Colors.DarkRed)
                                    .textColor(Microsoft.Maui.Graphics.Colors.White)
                                    .padding(12.).verticalOptions(Microsoft.Maui.Controls.LayoutOptions.End)
                                    .onTapped(DismissError)
                | None -> ()
            })
        )
    )
```

(If `onTapped` on Label isn't available, wrap in a `Button`-styled tap target or use the DSL's gesture recognizer — mechanical adaptation.)

- [ ] **Step 5: Rewrite `MauiProgram.fs` around the template's bootstrap**

Keep the template's `CreateMauiApp`/builder shape; replace its program with:

```fsharp
module FixItHere.Customer.MauiProgram

open System.Threading.Tasks
open Fabulous
open Fabulous.Maui
open Microsoft.Maui.Devices
open Microsoft.Maui.Hosting
open Microsoft.Maui.Media
open FixItHere.Customer

let private pickPhoto () : Task<Result<string, string>> =
    task {
        try
            let! file = MediaPicker.Default.PickPhotoAsync()
            if isNull (box file) then return Error "No photo selected"
            else
                use! stream = file.OpenReadAsync()
                use ms = new System.IO.MemoryStream()
                do! stream.CopyToAsync(ms)
                let bytes = ms.ToArray()
                if bytes.Length > 100_000 then return Error "Photo too large — pick a smaller one"
                else return Ok (System.Convert.ToBase64String bytes)
        with ex -> return Error ex.Message
    }

let private gpsLocation () : Task<Result<float * float, string>> =
    task {
        let! loc = Location.getCurrent (43.65, -79.38)
        return Ok loc
    }

let private deps =
    if DeviceInfo.Platform = DevicePlatform.Android then Config.baseUrl <- "http://10.0.2.2:5000"
    Api.createDepsWith pickPhoto gpsLocation (new System.Net.Http.HttpClientHandler()) Config.baseUrl

let mutable private hubStarted = false

/// Wraps Update.update: first successful login also starts the SignalR hub.
let private updateWithHub (msg: Msg) (model: Model) =
    let m, cmd = Update.update deps msg model
    match msg with
    | LoggedIn _ when not hubStarted ->
        hubStarted <- true
        let hubCmd =
            Cmd.ofEffect (fun dispatch ->
                Hub.HubClient(Config.baseUrl).Start(dispatch) |> ignore)
        m, Cmd.batch [ cmd; hubCmd ]
    | _ -> m, cmd

let program = Program.statefulWithCmd Update.init updateWithHub Views.Root.view

type MauiProgram =
    static member CreateMauiApp() =
        MauiApp.CreateBuilder()
            .UseFabulousApp(program)
            .Build()
```

(`Program.statefulWithCmd`'s arity and `UseFabulousApp` shape come from the template — adapt mechanically. If `Cmd.ofEffect` is missing, the installed Fabulous's dispatch-capturing Cmd constructor from the template's docs applies.)

Update the fsproj compile order to: `Config.fs`, `Domain.fs`, `Api.fs`, `Update.fs`, `Location.fs`, `Hub.fs`, `Views/Splash.fs`, `Views/Login.fs`, `Views/Home.fs`, `Views/Catalog.fs`, `Views/ProviderList.fs`, `Views/ProviderProfile.fs`, `Views/Root.fs`, `MauiProgram.fs` — and remove the template's sample files.

- [ ] **Step 6: Build, run, verify, commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
```

Start the backend (`ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Backend.Api --no-launch-profile`), launch the Catalyst app (`dotnet build -t:Run -f net10.0-maccatalyst src/Customer.Mobile`), and verify: Splash → Login shows the 5 customers → tapping John lands on Home with seeded active jobs listed → Catalog shows the 7 services → a service shows nearby providers with ratings/distances → provider profile shows feedback. Then:

```bash
git add -A && git commit -m "feat: Customer.Mobile views part 1 (login through provider profile) with app bootstrap"
```

### Task 8: Views part 2 — MapHtml, Booking, Tracking, Chat

**Files:**
- Create: `src/Customer.Mobile/Views/MapHtml.fs` (compile before Views/Root.fs, after Hub.fs), `Views/Booking.fs`, `Views/Tracking.fs`, `Views/Chat.fs`
- Modify: `src/Customer.Mobile/Views/Root.fs` (replace three placeholders)

**Interfaces:**
- Consumes: `Config.baseUrl`, `Geo.distanceKm`, `Model.ProviderPositions`, `Msg` cases incl. `PickAndSendPhoto`.
- Produces: `MapHtml.render : jobLat: float -> jobLng: float -> providerId: int -> string` (self-contained HTML; the page opens its OWN SignalR connection to `Config.baseUrl` and animates the car marker — no F#→JS bridge needed).

- [ ] **Step 1: Implement `Views/MapHtml.fs`**

```fsharp
module FixItHere.Customer.Views.MapHtml

open FixItHere.Customer

/// Self-contained Leaflet page: customer pin fixed, provider car marker driven by
/// the page's own SignalR connection (mirrors the /dev console pattern).
let render (jobLat: float) (jobLng: float) (providerId: int) : string =
    sprintf """<!doctype html><html><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>html,body,#map{margin:0;height:100%%;}</style></head>
<body><div id="map"></div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script src="https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script>
const jobPos = [%f, %f], providerId = %d, baseUrl = "%s";
const map = L.map("map").setView(jobPos, 12);
L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png").addTo(map);
L.marker(jobPos).addTo(map).bindPopup("You");
const car = L.circleMarker(jobPos, { radius: 9, color: "#1565c0", fillOpacity: 0.9 }).addTo(map);
let target = null, current = null;
function step() {
  if (target) {
    current = current || target;
    current = [current[0] + (target[0]-current[0]) * 0.2, current[1] + (target[1]-current[1]) * 0.2];
    car.setLatLng(current);
  }
  requestAnimationFrame(step);
}
step();
const conn = new signalR.HubConnectionBuilder().withUrl(baseUrl + "/hub").withAutomaticReconnect().build();
conn.on("LocationUpdated", l => { if (l.providerId === providerId) target = [l.lat, l.lng]; });
conn.start();
setTimeout(() => map.invalidateSize(), 400);
</script></body></html>""" jobLat jobLng providerId Config.baseUrl
```

- [ ] **Step 2: Implement `Views/Booking.fs`**

```fsharp
module FixItHere.Customer.Views.Booking

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let schedules = [ "Now"; "30 minutes"; "Tomorrow"; "Saturday" ]

let view (_model: Model) (providerId: int) (serviceId: int) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("When should they come?").font(size = 24.)
        for s in schedules do
            Button(s, BookJob (providerId, serviceId, s))
    }).padding(24.)
```

- [ ] **Step 3: Implement `Views/Tracking.fs`**

```fsharp
module FixItHere.Customer.Views.Tracking

open Fabulous.Maui
open type Fabulous.Maui.View
open Microsoft.Maui.Controls
open FixItHere.Customer

let private statusLine (state: string) =
    match state with
    | "Scheduled" -> "Waiting for provider to head out…"
    | "EnRoute" -> "Your provider is on the way"
    | "Arrived" -> "Your provider has arrived"
    | "InProgress" -> "Work in progress"
    | "Completed" -> "Job complete!"
    | s -> s

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None -> (VStack(spacing = 12.) { Button("← Back", GoBack); Label("Job not found") }).padding(24.)
    | Some job ->
        let etaLine =
            match model.ProviderPositions.TryFind job.ProviderId with
            | Some pos ->
                let km = Geo.distanceKm pos (job.Lat, job.Lng)
                sprintf "%.1f km away — ETA ~%d min" km (max 1 (int (km / 40.0 * 60.0)))
            | None -> "Locating provider…"
        (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
            (VStack(spacing = 4.) {
                Button("← Back", GoBack)
                Label(statusLine job.State).font(size = 20.)
                Label(sprintf "%s — %s ($%M)" job.ProviderName job.ServiceName job.Price)
                Label(etaLine)
            }).gridRow(0)
            WebView(HtmlWebViewSource(Html = MapHtml.render job.Lat job.Lng job.ProviderId)).gridRow(1)
            (HStack(spacing = 8.) {
                Button("Call", StartFakeCall)
                Button("Chat", Navigate (Chat job.Id))
                Button("Cancel Job", CancelActiveJob job.Id)
            }).gridRow(2)
        }).padding(12.)
```

(Exact `WebView`/`HtmlWebViewSource` widget shape per installed Fabulous — mechanical adaptation. The page needs internet for OSM tiles/CDN, same as `/dev`.)

- [ ] **Step 4: Implement `Views/Chat.fs`**

Chat draft lives in `Model.ChatDraft` (set via `ChatDraftChanged`; cleared by `update` on send):

```fsharp
module FixItHere.Customer.Views.Chat

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (jobId: int) =
    (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
        (VStack(spacing = 4.) {
            Button("← Back", GoBack)
            Label("Chat").font(size = 22.)
        }).gridRow(0)
        (ScrollView(
            (VStack(spacing = 6.) {
                for m in model.Messages |> List.filter (fun m -> m.JobId = jobId) do
                    let mine = model.Session |> Option.exists (fun s -> s.UserId = m.SenderId)
                    let prefix = if mine then "You" else m.SenderName
                    if System.String.IsNullOrEmpty m.PhotoBase64 then
                        Label(sprintf "%s: %s" prefix m.Text)
                    else
                        Label(sprintf "%s: [photo]" prefix)
            })
        )).gridRow(1)
        (HStack(spacing = 8.) {
            Entry(model.ChatDraft, ChatDraftChanged)
            Button("Send", SendChatMessage (jobId, model.ChatDraft, null))
            Button("📷", PickAndSendPhoto jobId)
        }).gridRow(2)
    }).padding(12.)
```

(`Entry(text, onTextChanged)` shape per installed Fabulous — the handler dispatches `ChatDraftChanged` with the new text; adapt the exact overload mechanically.)

- [ ] **Step 5: Replace the Booking/Tracking/Chat placeholders in `Views/Root.fs`**

```fsharp
    | Booking (pid, sid) -> AnyView(Booking.view model pid sid)
    | Tracking id -> AnyView(Tracking.view model id)
    | Chat id -> AnyView(Chat.view model id)
```

Add the new files to the fsproj compile order: `Views/MapHtml.fs` right after `Hub.fs`; `Views/Booking.fs`, `Views/Tracking.fs`, `Views/Chat.fs` before `Views/Root.fs`.

- [ ] **Step 6: Build, run, verify, commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
```

With the backend running: book a job from the app (John → Plumbing → a provider → Book → "Now"), confirm the Tracking screen appears with the map; in the `/dev` console press `accept`/`enroute` on the new job and use "Move Provider" map-clicks — the car marker on the phone app must move; send a chat message from `/dev` ("Inject Message") and see it appear in the app's Chat; reply from the app and see it in `/dev`'s live events. Then:

```bash
git add -A && git commit -m "feat: Customer.Mobile booking, live tracking map, and chat"
```

### Task 9: Views part 3 — Payment, Rating, DevSettings, fake-call overlay

**Files:**
- Create: `src/Customer.Mobile/Views/Payment.fs`, `Views/Rating.fs`, `Views/DevSettings.fs` (compile before Views/Root.fs)
- Modify: `src/Customer.Mobile/Views/Root.fs` (replace remaining placeholders + fake-call overlay)

**Interfaces:**
- Consumes: `Model.PaymentResult`, `Model.FakeCallActive`, `Msg` cases `SubmitRating`, `SetLocation`, `SetUseRealGps`, `EndFakeCall`.

- [ ] **Step 1: Implement `Views/Payment.fs`**

Two-phase UX is client-side (per the parent spec's compromise note): phase 1 renders while `PaymentResult = None` (the 2s `PaymentDelayDone` Cmd is already scheduled by `update` on entering the screen); phase 2 renders the result.

```fsharp
module FixItHere.Customer.Views.Payment

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (jobId: int) =
    (VStack(spacing = 16.) {
        match model.PaymentResult with
        | None ->
            Label("Payment Authorized").font(size = 28.).centerTextHorizontal()
            ActivityIndicator(true)
            Label("Processing…").centerTextHorizontal()
        | Some r ->
            Label("✓ Transferred to Provider").font(size = 28.).centerTextHorizontal()
            Label(sprintf "$%M" r.Amount).font(size = 40.).centerTextHorizontal()
            (VStack(spacing = 4.) {
                Label("— Receipt —").centerTextHorizontal()
                Label(sprintf "Job #%d" r.JobId).centerTextHorizontal()
                Label(sprintf "Status: %s" r.Status).centerTextHorizontal()
            })
            Button("Rate your experience", Navigate (Rating jobId))
    }).centerVertical().padding(24.)
```

- [ ] **Step 2: Implement `Views/Rating.fs`**

Star selection and comment live in `Model.RatingStars` / `Model.RatingComment` (reset by `update` after submission):

```fsharp
module FixItHere.Customer.Views.Rating

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (jobId: int) =
    (VStack(spacing = 16.) {
        Label("How was it?").font(size = 28.).centerTextHorizontal()
        (HStack(spacing = 4.) {
            for i in 1 .. 5 do
                Button((if i <= model.RatingStars then "★" else "☆"), StarsChanged i)
        }).centerHorizontal()
        Entry(model.RatingComment, RatingCommentChanged)
        Button("Submit", SubmitRating (jobId, model.RatingStars, model.RatingComment))
    }).centerVertical().padding(24.)
```

- [ ] **Step 3: Implement `Views/DevSettings.fs`**

```fsharp
module FixItHere.Customer.Views.DevSettings

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let cities = [ "Toronto", (43.6532, -79.3832); "Mississauga", (43.5890, -79.6441); "Brampton", (43.7315, -79.7624) ]

let view (model: Model) =
    let lat, lng = model.MyLocation
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("Developer Settings").font(size = 24.)
        Label(sprintf "Current location: %.4f, %.4f" lat lng)
        Label(if model.UseRealGps then "Mode: Real GPS" else "Mode: Simulated GPS")
        Button("Use Real GPS", SetUseRealGps true)
        Button("Use Simulated GPS", SetUseRealGps false)
        Label("Teleport to:").font(size = 18.)
        for (name, pos) in cities do
            Button(name, SetLocation pos)
    }).padding(24.)
```

- [ ] **Step 4: Root — replace placeholders + fake-call overlay**

```fsharp
    | Payment id -> AnyView(Payment.view model id)
    | Rating id -> AnyView(Rating.view model id)
    | DevSettings -> AnyView(DevSettings.view model)
```

And add to Root's overlay Grid (after the error overlay):

```fsharp
                if model.FakeCallActive then
                    (VStack(spacing = 16.) {
                        Label("Calling provider…").font(size = 28.).textColor(Microsoft.Maui.Graphics.Colors.White).centerTextHorizontal()
                        ActivityIndicator(true)
                        Button("End Call", EndFakeCall)
                    }).backgroundColor(Microsoft.Maui.Graphics.Color.FromRgba(0., 0., 0., 0.85)).centerVertical()
```

Delete the `placeholder` helper once unused. Add the three new files to the fsproj compile order before `Views/Root.fs`.

- [ ] **Step 5: Build, run, verify, commit**

```bash
dotnet test tests/Customer.Mobile.Tests
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
```

With the backend running: drive a booked job to `complete` from the `/dev` console → the app must auto-advance Tracking → Payment ("Payment Authorized" → receipt) → Rate (stars + comment) → back Home with the job gone from the active list and a toast. Tap Call on a tracking screen → overlay shows ~10s → auto-dismisses. DevSettings: teleport to Brampton and confirm the provider list re-sorts on next catalog visit. Then:

```bash
git add -A && git commit -m "feat: Customer.Mobile payment, rating, dev settings, fake call"
```

### Task 10: Full-flow verification + README

**Files:**
- Modify: `README.md` (Customer.Mobile run instructions)

- [ ] **Step 1: Full suite + both-project build**

```bash
dotnet test
dotnet build src/Customer.Mobile -f net10.0-maccatalyst
```

Expected: all Shared.Tests, Backend.Api.Tests, and Customer.Mobile.Tests pass; app builds.

- [ ] **Step 2: Scripted end-to-end demo (the acceptance walk)**

Backend running; Catalyst app running; `/dev` console open in a browser.

1. App: login as **John** → Home shows seeded active jobs.
2. App: Book a New Service → Plumbing → nearest provider → Book → **Now** → Tracking appears.
3. `/dev`: the new job appears — press **accept**, then **enroute**; click the map to move the provider → the app's car marker glides; status banner reads "on the way"; ETA/distance update.
4. `/dev`: inject a message as Provider → app Chat shows it; reply from the app → `/dev` live events shows it.
5. App: tap **Call** → "Calling provider…" → auto "ends" after ~10s.
6. `/dev`: press **arrive** → app banner updates + notification toast; press **start**, then **complete** → app auto-advances to Payment → "Payment Authorized" → "$85.00 Transferred" receipt → Rate 5★ + comment → Submit → Home, job gone, toast shown.
7. `/dev`: press **▶ Start Demo** (with the app on Home) → watch the whole scripted flow arrive as toasts/job updates in the app.

Any step failing = stop, fix, re-verify before proceeding.

- [ ] **Step 3: README update**

Add under "Run the backend + demo control panel":

```markdown
## Run the Customer app (Mac Catalyst)

    dotnet build -t:Run -f net10.0-maccatalyst src/Customer.Mobile

Requires the backend running (above). Log in as John/Mary/Steve/Susan/Bob.
Use the /dev console as the "provider side" to accept/drive jobs, or press
Start Demo there for the fully scripted flow.

Android emulator: the app auto-targets http://10.0.2.2:5000.
```

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: Customer.Mobile run instructions; full-flow verification complete"
```

---

## Self-review notes

- **Spec coverage:** 12 screens ✔ (T7 six + Root, T8 three, T9 three); single-Model screen-swap nav ✔ (T2/T4); `ApiDeps` seam + headless linked-file tests ✔ (T2–T5); SignalR subscription shared across screens ✔ (T6/T7 wiring, T5 patches); Leaflet map with animated car ✔ (T8, page-owned hub connection — deviation from the spec's F#→JS `EvaluateJavaScriptAsync` mechanism, chosen to keep MVU pure and reuse the proven `/dev` pattern; the spec's stated goal "marker animates on LocationUpdated" is met); fake call ✔ (T9); client-side two-phase payment ✔ (T9); rating closes job via backend ✔ (T5/T9); DevSettings real/simulated GPS + city teleport ✔ (T9); photo capture ≤100KB base64 ✔ (T7 MauiProgram pickPhoto + T8 Chat), 5-photo cap enforced in update (T5); toasts from `Notification` ✔ (T5/T7); Android cleartext + 10.0.2.2 ✔ (T1/T7); TFM fallback documented ✔ (T1); README ✔ (T10). Deferred per spec: Auto-Reply, Typing/Seen, route slider (Plan 3).
- **Type consistency check:** `ApiDeps` fields used in `Update.fs` match Task 2's record (incl. `PickPhoto`/`GetGpsLocation` — see contract edits noted below); `Msg` cases in views (`SelectCustomer`, `Navigate`, `BookJob`, `CancelActiveJob`, `SendChatMessage`, `PickAndSendPhoto`, `SubmitRating`, `SetLocation`, `SetUseRealGps`, `StartFakeCall`/`EndFakeCall`, `DismissToast`/`DismissError`) all exist in Task 2's DU; `Views.Root.view` consumed by Task 7's `MauiProgram.program`.
- **Known honest risks (not placeholders):** exact Fabulous 8 DSL names (widget ctors, modifiers, `Program`/`Cmd` arities) and `AnyView` availability are version-dependent — governed by the Execution profile's scoped exception with mandatory commit-body notes. Chat draft and rating stars/comment are proper Model state (`ChatDraft`/`RatingStars`/`RatingComment`) with dedicated Msg cases, so no view-local mutability exists anywhere.
