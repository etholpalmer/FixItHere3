# FixItHere.Demo — Plan 1: Shared Domain + Backend.Api + /dev Console

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure F# domain library (`Shared`), the F# ASP.NET Core backend (`Backend.Api`) with SQLite + deterministic seed + SignalR, and the dev-only `/dev` Demo Control Panel — so the entire marketplace flow is drivable end-to-end with zero mobile code.

**Architecture:** `Shared` is a dependency-free F# library holding domain types, DTOs, and a pure Job state machine. `Backend.Api` wraps it with EF Core/SQLite (drop+recreate+reseed on every boot), ~18 Minimal API endpoints under a `{success, data, error}` envelope, one SignalR hub (`DemoHub`), and a static HTML/JS control panel at `/dev` (Development environment only) that consumes the exact same REST+SignalR contract the future mobile apps will use.

**Tech Stack:** .NET 8, F#, ASP.NET Core Minimal APIs, EF Core + SQLite (`Microsoft.EntityFrameworkCore.Sqlite`), SignalR, xUnit + FsCheck, `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** `docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`

## Global Constraints

- Solution name: `FixItHere.Demo.sln`; projects live under `/src` (`Shared`, `Backend.Api`); tests under `/tests`.
- All F#. No C# projects.
- Catalog services (exactly 7, in this order): Plumbing, Electrical, Painting, Mechanic, Moving, Cleaning, HVAC.
- Named customers: John, Mary, Steve, Susan, Bob. Named providers: Mike's Plumbing (Plumbing), Joe Electric (Electrical), Rapid Tire Repair (Mechanic), Elite HVAC (HVAC).
- Seed: 20 customers, 20 providers, 50 completed/closed jobs, 30 pending (`Scheduled`) jobs, ratings, messages. **Deterministic** — no `DateTime.Now`, no unseeded `Random`; use a fixed epoch `2026-01-01T00:00:00Z` and `Random(42)`.
- Every startup: `EnsureDeleted()` → `EnsureCreated()` → reseed.
- Response envelope everywhere: `{ "success": bool, "data": ..., "error": string|null }`.
- Invalid state transitions return HTTP 409 with a domain error message, never a 500.
- `/dev` console and all `/dev/*` endpoints are mapped **only** when `app.Environment.IsDevelopment()`.
- No Stripe, no real auth (fake JWT = plain token string `"fake-{role}-{id}"`), no external services.
- Commit after every green test cycle; conventional commit messages; **no AI attribution trailers**.

---

### Task 1: Solution scaffold + Shared project + domain types

**Files:**
- Create: `FixItHere.Demo.sln`, `src/Shared/Shared.fsproj`, `src/Shared/Domain.fs`, `tests/Shared.Tests/Shared.Tests.fsproj`, `tests/Shared.Tests/DomainTests.fs`

**Interfaces:**
- Produces: namespace `FixItHere.Shared` — `JobState` DU, `JobEvent` DU, `JobKind`, `ServiceNames.all : string list` (the 7 services).

- [ ] **Step 1: Scaffold solution and projects**

```bash
cd /Users/etholpalmer/Dev/TCAC-Systems/Flutter/FixItHere3
dotnet new sln -n FixItHere.Demo
dotnet new classlib -lang F# -o src/Shared -n Shared -f net8.0
dotnet new xunit -lang F# -o tests/Shared.Tests -n Shared.Tests -f net8.0
dotnet sln add src/Shared tests/Shared.Tests
dotnet add tests/Shared.Tests reference src/Shared
dotnet add tests/Shared.Tests package FsCheck.Xunit
```

Delete the template files `src/Shared/Library.fs` and `tests/Shared.Tests/Tests.fs` references will be replaced below.

- [ ] **Step 2: Write failing test for domain types**

Replace `tests/Shared.Tests/Tests.fs` with `tests/Shared.Tests/DomainTests.fs` (update the fsproj `<Compile Include>` accordingly):

```fsharp
module FixItHere.Shared.Tests.DomainTests

open Xunit
open FixItHere.Shared

[<Fact>]
let ``there are exactly seven catalog services ending with HVAC`` () =
    Assert.Equal(7, List.length ServiceNames.all)
    Assert.Equal<string>(
        ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"],
        ServiceNames.all)
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Shared.Tests` — Expected: FAIL (namespace `FixItHere.Shared` not defined).

- [ ] **Step 4: Implement domain types**

Replace `src/Shared/Library.fs` with `src/Shared/Domain.fs` (update fsproj):

```fsharp
namespace FixItHere.Shared

type JobState =
    | Scheduled
    | EnRoute
    | Arrived
    | InProgress
    | Completed
    | Closed
    | Cancelled

type JobEvent =
    | Accepted      // provider takes the job (stays Scheduled in demo; accept marks assignment)
    | DepartEnRoute
    | Arrive
    | StartWork
    | CompleteWork
    | RateAndClose
    | Cancel

type JobKind = Service

module ServiceNames =
    let all = ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"]
```

- [ ] **Step 5: Run tests — PASS, then commit**

```bash
dotnet test tests/Shared.Tests
git add -A && git commit -m "feat: scaffold solution with Shared domain types"
```

### Task 2: Pure Job state machine

**Files:**
- Create: `src/Shared/StateMachine.fs`
- Test: `tests/Shared.Tests/StateMachineTests.fs`

**Interfaces:**
- Produces: `module FixItHere.Shared.StateMachine` with
  `transition : JobState -> JobEvent -> Result<JobState, string>`.

- [ ] **Step 1: Write failing tests (exhaustive happy path + rejections + property)**

`tests/Shared.Tests/StateMachineTests.fs` (add to fsproj after DomainTests):

```fsharp
module FixItHere.Shared.Tests.StateMachineTests

open Xunit
open FsCheck.Xunit
open FixItHere.Shared
open FixItHere.Shared.StateMachine

[<Fact>]
let ``happy path walks scheduled to closed`` () =
    let step st ev = Result.bind (fun s -> transition s ev) st
    let final =
        Ok Scheduled
        |> fun s -> step s DepartEnRoute
        |> fun s -> step s Arrive
        |> fun s -> step s StartWork
        |> fun s -> step s CompleteWork
        |> fun s -> step s RateAndClose
    Assert.Equal(Ok Closed, final)

[<Fact>]
let ``cannot start work before arriving`` () =
    match transition EnRoute StartWork with
    | Error msg -> Assert.Contains("EnRoute", msg)
    | Ok s -> failwithf "expected rejection, got %A" s

[<Theory>]
[<InlineData("Scheduled")>] [<InlineData("EnRoute")>]
[<InlineData("Arrived")>]   [<InlineData("InProgress")>]
let ``cancel allowed from any pre-completed state`` (name: string) =
    let st =
        match name with
        | "Scheduled" -> Scheduled | "EnRoute" -> EnRoute
        | "Arrived" -> Arrived | _ -> InProgress
    Assert.Equal(Ok Cancelled, transition st Cancel)

[<Property>]
let ``terminal states accept no events`` (ev: JobEvent) =
    [Closed; Cancelled]
    |> List.forall (fun st ->
        match transition st ev with Error _ -> true | Ok _ -> false)
```

- [ ] **Step 2: Run — FAIL** (`StateMachine` not defined): `dotnet test tests/Shared.Tests`

- [ ] **Step 3: Implement**

`src/Shared/StateMachine.fs` (compile after Domain.fs):

```fsharp
module FixItHere.Shared.StateMachine

open FixItHere.Shared

/// Pure transition function — the spine of the demo.
let transition (state: JobState) (event: JobEvent) : Result<JobState, string> =
    match state, event with
    | Scheduled,  Accepted      -> Ok Scheduled   // acceptance = assignment, state unchanged
    | Scheduled,  DepartEnRoute -> Ok EnRoute
    | EnRoute,    Arrive        -> Ok Arrived
    | Arrived,    StartWork     -> Ok InProgress
    | InProgress, CompleteWork  -> Ok Completed
    | Completed,  RateAndClose  -> Ok Closed
    | (Scheduled | EnRoute | Arrived | InProgress), Cancel -> Ok Cancelled
    | s, e -> Error (sprintf "Invalid transition: cannot apply %A while %A" e s)
```

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Shared.Tests
git add -A && git commit -m "feat: add pure Job state machine with property tests"
```

### Task 3: DTOs + response envelope in Shared

**Files:**
- Create: `src/Shared/Dtos.fs`
- Test: `tests/Shared.Tests/DtoTests.fs`

**Interfaces:**
- Produces (namespace `FixItHere.Shared.Dtos`, all CLIMutable records serialized as camelCase JSON by ASP.NET defaults):
  - `Envelope<'t> = { Success: bool; Data: 't; Error: string }` with `Envelope.ok : 't -> Envelope<'t>` and `Envelope.fail : string -> Envelope<obj>`
  - `LoginRequest = { Role: string; Name: string }`, `LoginResponse = { Token: string; UserId: int; Role: string; DisplayName: string }`
  - `ServiceDto = { Id: int; Name: string }`
  - `ProviderDto = { Id: int; BusinessName: string; ServiceId: int; ServiceName: string; Rating: float; RatingCount: int; Lat: float; Lng: float; Online: bool; Vehicle: string; PhotoUrl: string }`
  - `JobDto = { Id: int; CustomerId: int; CustomerName: string; ProviderId: int; ProviderName: string; ServiceId: int; ServiceName: string; State: string; Price: decimal; ScheduledFor: string; Lat: float; Lng: float; Address: string }`
  - `CreateJobRequest = { CustomerId: int; ProviderId: int; ServiceId: int; ScheduleChoice: string; Lat: float; Lng: float; Address: string }` (`ScheduleChoice` ∈ `"Now" | "30 minutes" | "Tomorrow" | "Saturday"`)
  - `MessageDto = { Id: int; JobId: int; SenderId: int; SenderName: string; Text: string; PhotoBase64: string; SentAt: string; Seen: bool }`
  - `SendMessageRequest = { JobId: int; SenderId: int; Text: string; PhotoBase64: string }`
  - `RatingDto = { Id: int; JobId: int; RaterId: int; RateeId: int; Stars: int; Comment: string }`
  - `CreateRatingRequest = { JobId: int; RaterId: int; RateeId: int; Stars: int; Comment: string }`
  - `LocationDto = { ProviderId: int; Lat: float; Lng: float; UpdatedAt: string }`
  - `UpdateLocationRequest = { ProviderId: int; Lat: float; Lng: float }`
  - `PaymentRequest = { JobId: int }`, `PaymentResult = { JobId: int; Amount: decimal; Status: string }` (`Status` = `"Authorized"` then `"Transferred"`)

- [ ] **Step 1: Failing test**

`tests/Shared.Tests/DtoTests.fs`:

```fsharp
module FixItHere.Shared.Tests.DtoTests

open Xunit
open FixItHere.Shared.Dtos

[<Fact>]
let ``Envelope.ok wraps data`` () =
    let e = Envelope.ok 42
    Assert.True(e.Success)
    Assert.Equal(42, e.Data)
    Assert.Null(e.Error)

[<Fact>]
let ``Envelope.fail carries message`` () =
    let e = Envelope.fail "boom"
    Assert.False(e.Success)
    Assert.Equal("boom", e.Error)
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Shared.Tests`

- [ ] **Step 3: Implement `src/Shared/Dtos.fs`**

```fsharp
namespace FixItHere.Shared.Dtos

[<CLIMutable>] type Envelope<'t> = { Success: bool; Data: 't; Error: string }
module Envelope =
    let ok data = { Success = true; Data = data; Error = null }
    let fail (msg: string) : Envelope<obj> = { Success = false; Data = null; Error = msg }

[<CLIMutable>] type LoginRequest = { Role: string; Name: string }
[<CLIMutable>] type LoginResponse = { Token: string; UserId: int; Role: string; DisplayName: string }
[<CLIMutable>] type ServiceDto = { Id: int; Name: string }
[<CLIMutable>] type ProviderDto =
    { Id: int; BusinessName: string; ServiceId: int; ServiceName: string
      Rating: float; RatingCount: int; Lat: float; Lng: float
      Online: bool; Vehicle: string; PhotoUrl: string }
[<CLIMutable>] type JobDto =
    { Id: int; CustomerId: int; CustomerName: string
      ProviderId: int; ProviderName: string
      ServiceId: int; ServiceName: string
      State: string; Price: decimal; ScheduledFor: string
      Lat: float; Lng: float; Address: string }
[<CLIMutable>] type CreateJobRequest =
    { CustomerId: int; ProviderId: int; ServiceId: int
      ScheduleChoice: string; Lat: float; Lng: float; Address: string }
[<CLIMutable>] type MessageDto =
    { Id: int; JobId: int; SenderId: int; SenderName: string
      Text: string; PhotoBase64: string; SentAt: string; Seen: bool }
[<CLIMutable>] type SendMessageRequest =
    { JobId: int; SenderId: int; Text: string; PhotoBase64: string }
[<CLIMutable>] type RatingDto =
    { Id: int; JobId: int; RaterId: int; RateeId: int; Stars: int; Comment: string }
[<CLIMutable>] type CreateRatingRequest =
    { JobId: int; RaterId: int; RateeId: int; Stars: int; Comment: string }
[<CLIMutable>] type LocationDto = { ProviderId: int; Lat: float; Lng: float; UpdatedAt: string }
[<CLIMutable>] type UpdateLocationRequest = { ProviderId: int; Lat: float; Lng: float }
[<CLIMutable>] type PaymentRequest = { JobId: int }
[<CLIMutable>] type PaymentResult = { JobId: int; Amount: decimal; Status: string }
```

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Shared.Tests
git add -A && git commit -m "feat: add shared DTOs and response envelope"
```

### Task 4: Backend.Api project + EF Core entities + DbContext

**Files:**
- Create: `src/Backend.Api/Backend.Api.fsproj`, `src/Backend.Api/Db.fs`, minimal `src/Backend.Api/Program.fs`
- Create: `tests/Backend.Api.Tests/Backend.Api.Tests.fsproj`, `tests/Backend.Api.Tests/DbTests.fs`

**Interfaces:**
- Produces (namespace `FixItHere.Backend.Db`): CLIMutable entities `Customer`, `Provider`, `Service`, `Job`, `Message`, `Rating` and `AppDb : DbContext` with `DbSet` members `Customers`, `Providers`, `Services`, `Jobs`, `Messages`, `Ratings`. `Job.State : string` stores `JobState` name (e.g. `"Scheduled"`); `JobStateCodec.toState/ofState` convert.

- [ ] **Step 1: Scaffold**

```bash
dotnet new web -lang F# -o src/Backend.Api -n Backend.Api -f net8.0
dotnet new xunit -lang F# -o tests/Backend.Api.Tests -n Backend.Api.Tests -f net8.0
dotnet sln add src/Backend.Api tests/Backend.Api.Tests
dotnet add src/Backend.Api reference src/Shared
dotnet add src/Backend.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add tests/Backend.Api.Tests reference src/Backend.Api
dotnet add tests/Backend.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Backend.Api.Tests package Microsoft.EntityFrameworkCore.Sqlite
```

- [ ] **Step 2: Failing test — context round-trips a Job in in-memory SQLite**

`tests/Backend.Api.Tests/DbTests.fs` (replace template Tests.fs):

```fsharp
module FixItHere.Backend.Tests.DbTests

open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open FixItHere.Backend.Db

let makeDb () =
    let conn = new SqliteConnection("DataSource=:memory:")
    conn.Open()
    let opts = DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options
    let db = new AppDb(opts)
    db.Database.EnsureCreated() |> ignore
    db, conn

[<Fact>]
let ``job round-trips through sqlite`` () =
    let db, conn = makeDb ()
    use _ = conn
    let job =
        { Id = 0; CustomerId = 1; ProviderId = 2; ServiceId = 3
          State = "Scheduled"; Price = 85.00m
          ScheduledFor = "2026-01-01T09:00:00Z"
          Lat = 43.65; Lng = -79.38; Address = "1 Yonge St, Toronto" }
    db.Jobs.Add(job) |> ignore
    db.SaveChanges() |> ignore
    let loaded = db.Jobs.Single()
    Assert.Equal("Scheduled", loaded.State)
    Assert.Equal(85.00m, loaded.Price)
```

- [ ] **Step 3: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 4: Implement `src/Backend.Api/Db.fs`** (compiled before Program.fs)

```fsharp
namespace FixItHere.Backend.Db

open Microsoft.EntityFrameworkCore
open FixItHere.Shared

[<CLIMutable>] type Service = { Id: int; Name: string }
[<CLIMutable>] type Customer = { Id: int; Name: string; Lat: float; Lng: float }
[<CLIMutable>] type Provider =
    { Id: int; BusinessName: string; ServiceId: int
      Lat: float; Lng: float; Online: bool; Vehicle: string; PhotoUrl: string }
[<CLIMutable>] type Job =
    { Id: int; CustomerId: int; ProviderId: int; ServiceId: int
      State: string; Price: decimal; ScheduledFor: string
      Lat: float; Lng: float; Address: string }
[<CLIMutable>] type Message =
    { Id: int; JobId: int; SenderId: int; Text: string
      PhotoBase64: string; SentAt: string; Seen: bool }
[<CLIMutable>] type Rating =
    { Id: int; JobId: int; RaterId: int; RateeId: int; Stars: int; Comment: string }

module JobStateCodec =
    let ofState (s: JobState) = sprintf "%A" s
    let toState (s: string) : JobState =
        match s with
        | "Scheduled" -> Scheduled | "EnRoute" -> EnRoute | "Arrived" -> Arrived
        | "InProgress" -> InProgress | "Completed" -> Completed
        | "Closed" -> Closed | "Cancelled" -> Cancelled
        | other -> failwithf "Unknown job state '%s'" other

type AppDb(options: DbContextOptions<AppDb>) =
    inherit DbContext(options)
    member val Services  : DbSet<Service>  = Unchecked.defaultof<_> with get, set
    member val Customers : DbSet<Customer> = Unchecked.defaultof<_> with get, set
    member val Providers : DbSet<Provider> = Unchecked.defaultof<_> with get, set
    member val Jobs      : DbSet<Job>      = Unchecked.defaultof<_> with get, set
    member val Messages  : DbSet<Message>  = Unchecked.defaultof<_> with get, set
    member val Ratings   : DbSet<Rating>   = Unchecked.defaultof<_> with get, set
```

Keep `Program.fs` as the template minimal app for now (it must still build).

- [ ] **Step 5: Run — PASS, commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: add Backend.Api with EF Core SQLite entities"
```

### Task 5: Deterministic seeder

**Files:**
- Create: `src/Backend.Api/Seed.fs` (after Db.fs, before Program.fs)
- Test: `tests/Backend.Api.Tests/SeedTests.fs`

**Interfaces:**
- Produces: `module FixItHere.Backend.Seed` with `run : AppDb -> unit` and `Epoch : string` (= `"2026-01-01T00:00:00Z"`).

- [ ] **Step 1: Failing tests**

`tests/Backend.Api.Tests/SeedTests.fs`:

```fsharp
module FixItHere.Backend.Tests.SeedTests

open System.Linq
open Xunit
open FixItHere.Backend.Db
open FixItHere.Backend
open FixItHere.Backend.Tests.DbTests

[<Fact>]
let ``seed produces required counts`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    Assert.Equal(7,  db.Services.Count())
    Assert.Equal(20, db.Customers.Count())
    Assert.Equal(20, db.Providers.Count())
    Assert.Equal(50, db.Jobs.Count(fun j -> j.State = "Completed" || j.State = "Closed"))
    Assert.Equal(30, db.Jobs.Count(fun j -> j.State = "Scheduled"))
    Assert.True(db.Ratings.Count() > 0)
    Assert.True(db.Messages.Count() > 0)

[<Fact>]
let ``named personas exist with correct services`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    for name in ["John"; "Mary"; "Steve"; "Susan"; "Bob"] do
        Assert.True(db.Customers.Any(fun c -> c.Name = name), name)
    let svcId name = db.Services.Single(fun s -> s.Name = name).Id
    let check biz svc =
        Assert.Equal(svcId svc, db.Providers.Single(fun p -> p.BusinessName = biz).ServiceId)
    check "Mike's Plumbing" "Plumbing"
    check "Joe Electric" "Electrical"
    check "Rapid Tire Repair" "Mechanic"
    check "Elite HVAC" "HVAC"

[<Fact>]
let ``seed is deterministic across two runs`` () =
    let snapshot () =
        let db, conn = makeDb ()
        Seed.run db
        let s =
            db.Jobs.OrderBy(fun j -> j.Id)
            |> Seq.map (fun j -> sprintf "%d|%s|%M|%s" j.Id j.State j.Price j.ScheduledFor)
            |> String.concat ";"
        conn.Dispose()
        s
    Assert.Equal(snapshot (), snapshot ())
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 3: Implement `src/Backend.Api/Seed.fs`**

```fsharp
module FixItHere.Backend.Seed

open System
open FixItHere.Shared
open FixItHere.Backend.Db

let Epoch = "2026-01-01T00:00:00Z"

/// Deterministic: fixed name lists, Random(42), fixed epoch. No wall clock.
let run (db: AppDb) =
    let rng = Random(42)
    // GTA-ish bounding box for coordinates
    let lat () = 43.55 + rng.NextDouble() * 0.30
    let lng () = -79.75 + rng.NextDouble() * 0.55

    let services =
        ServiceNames.all |> List.map (fun n -> { Id = 0; Name = n })
    db.Services.AddRange services |> ignore
    db.SaveChanges() |> ignore
    let svc name = db.Services.Local |> Seq.find (fun s -> s.Name = name)

    let customerNames =
        [ "John"; "Mary"; "Steve"; "Susan"; "Bob"
          "Alice"; "Tom"; "Grace"; "Henry"; "Ivy"
          "Jack"; "Karen"; "Leo"; "Mona"; "Nate"
          "Olive"; "Paul"; "Quinn"; "Rita"; "Sam" ]
    db.Customers.AddRange(customerNames |> List.map (fun n ->
        { Id = 0; Name = n; Lat = lat (); Lng = lng () })) |> ignore

    let namedProviders =
        [ "Mike's Plumbing", "Plumbing", "White van"
          "Joe Electric", "Electrical", "Blue pickup"
          "Rapid Tire Repair", "Mechanic", "Service truck"
          "Elite HVAC", "HVAC", "Box truck" ]
    let fillerProviders =
        [ "Pro Painters Co", "Painting"; "Swift Movers", "Moving"
          "Sparkle Clean", "Cleaning"; "DrainMasters", "Plumbing"
          "Volt Bros", "Electrical"; "ColorWorks", "Painting"
          "GearHeads Mobile", "Mechanic"; "Box & Dolly", "Moving"
          "FreshNest Cleaning", "Cleaning"; "CoolFlow HVAC", "HVAC"
          "PipeDream Plumbing", "Plumbing"; "Amp It Up", "Electrical"
          "BrushStrokes", "Painting"; "WrenchWorks", "Mechanic"
          "HaulStars", "Moving"; "PolishPros", "Cleaning" ]
    let providers =
        (namedProviders |> List.map (fun (b, s, v) -> b, s, v))
        @ (fillerProviders |> List.map (fun (b, s) -> b, s, "Van"))
    db.Providers.AddRange(providers |> List.map (fun (biz, s, vehicle) ->
        { Id = 0; BusinessName = biz; ServiceId = (svc s).Id
          Lat = lat (); Lng = lng (); Online = true
          Vehicle = vehicle; PhotoUrl = sprintf "/img/provider-%d.png" (rng.Next(1, 9)) })) |> ignore
    db.SaveChanges() |> ignore

    let customers = db.Customers.Local |> Seq.toArray
    let provs = db.Providers.Local |> Seq.toArray
    let mkJob i state =
        let c = customers.[i % customers.Length]
        let p = provs.[(i * 3) % provs.Length]
        { Id = 0; CustomerId = c.Id; ProviderId = p.Id; ServiceId = p.ServiceId
          State = state
          Price = decimal (40 + rng.Next(0, 25) * 5)
          ScheduledFor = DateTimeOffset.Parse(Epoch).AddHours(float i).ToString("o")
          Lat = c.Lat; Lng = c.Lng
          Address = sprintf "%d Demo Street" (100 + i) }
    // 50 finished (alternate Completed/Closed), 30 pending
    let finished = [ for i in 0 .. 49 -> mkJob i (if i % 2 = 0 then "Closed" else "Completed") ]
    let pending  = [ for i in 50 .. 79 -> mkJob i "Scheduled" ]
    db.Jobs.AddRange(finished @ pending) |> ignore
    db.SaveChanges() |> ignore

    let comments = [ "Great work!"; "On time and professional."; "Would book again."; "Fixed it fast."; "Friendly and tidy." ]
    let doneJobs = db.Jobs.Local |> Seq.filter (fun j -> j.State = "Closed") |> Seq.toList
    db.Ratings.AddRange(doneJobs |> List.map (fun j ->
        { Id = 0; JobId = j.Id; RaterId = j.CustomerId; RateeId = j.ProviderId
          Stars = 3 + rng.Next(0, 3); Comment = comments.[rng.Next(comments.Length)] })) |> ignore

    db.Messages.AddRange(doneJobs |> List.truncate 20 |> List.map (fun j ->
        { Id = 0; JobId = j.Id; SenderId = j.CustomerId
          Text = "Hi, see you soon!"; PhotoBase64 = null
          SentAt = Epoch; Seen = true })) |> ignore
    db.SaveChanges() |> ignore
```

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: deterministic database seeder"
```

### Task 6: JobService + broadcasting abstraction

**Files:**
- Create: `src/Backend.Api/Services.fs` (after Seed.fs)
- Test: `tests/Backend.Api.Tests/JobServiceTests.fs`

**Interfaces:**
- Produces: `IBroadcaster` with members `JobUpdated: JobDto -> Task`, `MessageReceived: MessageDto -> Task`, `LocationUpdated: LocationDto -> Task`, `Notify: string -> Task`; `JobService(db: AppDb, hub: IBroadcaster)` with `Apply : jobId:int -> JobEvent -> Task<Result<JobDto, string>>` and `Create : CreateJobRequest -> Task<JobDto>`; `toJobDto : AppDb -> Db.Job -> JobDto` helper. `NullBroadcaster` no-op impl for tests.

- [ ] **Step 1: Failing tests**

`tests/Backend.Api.Tests/JobServiceTests.fs`:

```fsharp
module FixItHere.Backend.Tests.JobServiceTests

open System.Linq
open Xunit
open FixItHere.Shared
open FixItHere.Backend
open FixItHere.Backend.Services
open FixItHere.Backend.Tests.DbTests

let setup () =
    let db, conn = makeDb ()
    Seed.run db
    JobService(db, NullBroadcaster()), db, conn

[<Fact>]
let ``valid transition persists new state`` () =
    let svc, db, conn = setup ()
    use _ = conn
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")
    let result = (svc.Apply job.Id DepartEnRoute).Result
    match result with
    | Ok dto -> Assert.Equal("EnRoute", dto.State)
    | Error e -> failwith e
    Assert.Equal("EnRoute", db.Jobs.Single(fun j -> j.Id = job.Id).State)

[<Fact>]
let ``invalid transition returns Error and does not persist`` () =
    let svc, db, conn = setup ()
    use _ = conn
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")
    match (svc.Apply job.Id CompleteWork).Result with
    | Error msg -> Assert.Contains("Invalid transition", msg)
    | Ok _ -> failwith "expected Error"
    Assert.Equal("Scheduled", db.Jobs.Single(fun j -> j.Id = job.Id).State)
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 3: Implement `src/Backend.Api/Services.fs`**

```fsharp
module FixItHere.Backend.Services

open System
open System.Linq
open System.Threading.Tasks
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db

type IBroadcaster =
    abstract JobUpdated: JobDto -> Task
    abstract MessageReceived: MessageDto -> Task
    abstract LocationUpdated: LocationDto -> Task
    abstract Notify: string -> Task

type NullBroadcaster() =
    interface IBroadcaster with
        member _.JobUpdated _ = Task.CompletedTask
        member _.MessageReceived _ = Task.CompletedTask
        member _.LocationUpdated _ = Task.CompletedTask
        member _.Notify _ = Task.CompletedTask

let toJobDto (db: AppDb) (j: Job) : JobDto =
    let cust = db.Customers.Single(fun c -> c.Id = j.CustomerId)
    let prov = db.Providers.Single(fun p -> p.Id = j.ProviderId)
    let svc  = db.Services.Single(fun s -> s.Id = j.ServiceId)
    { Id = j.Id; CustomerId = j.CustomerId; CustomerName = cust.Name
      ProviderId = j.ProviderId; ProviderName = prov.BusinessName
      ServiceId = j.ServiceId; ServiceName = svc.Name
      State = j.State; Price = j.Price; ScheduledFor = j.ScheduledFor
      Lat = j.Lat; Lng = j.Lng; Address = j.Address }

type JobService(db: AppDb, hub: IBroadcaster) =
    member _.Apply (jobId: int) (event: JobEvent) : Task<Result<JobDto, string>> =
        task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = jobId) |> Option.ofObj with
            | None -> return Error (sprintf "Job %d not found" jobId)
            | Some job ->
                match StateMachine.transition (JobStateCodec.toState job.State) event with
                | Error e -> return Error e
                | Ok next ->
                    let updated = { job with State = JobStateCodec.ofState next }
                    db.Entry(job).CurrentValues.SetValues(updated)
                    db.SaveChanges() |> ignore
                    let dto = toJobDto db updated
                    do! hub.JobUpdated dto
                    return Ok dto
        }

    member _.Create (req: CreateJobRequest) : Task<JobDto> =
        task {
            let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
            let job =
                { Id = 0; CustomerId = req.CustomerId; ProviderId = req.ProviderId
                  ServiceId = req.ServiceId; State = "Scheduled"
                  Price = 85.00m
                  ScheduledFor = req.ScheduleChoice
                  Lat = req.Lat; Lng = req.Lng; Address = req.Address }
            ignore prov
            db.Jobs.Add job |> ignore
            db.SaveChanges() |> ignore
            let dto = toJobDto db (db.Jobs.OrderByDescending(fun j -> j.Id).First())
            do! hub.JobUpdated dto
            return dto
        }
```

Note: `Job` is CLIMutable, so `{ job with State = ... }` creates a copy; `SetValues` writes it back onto the tracked entity — keeps the domain-style immutability while satisfying EF.

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: JobService applying state machine with broadcast hook"
```

### Task 7: SignalR DemoHub + Program.fs wiring + startup reset/reseed

**Files:**
- Create: `src/Backend.Api/Hub.fs` (after Services.fs)
- Modify: `src/Backend.Api/Program.fs`
- Test: `tests/Backend.Api.Tests/AppFactory.fs` (shared test factory; endpoint tests come in Task 8)

**Interfaces:**
- Produces: `DemoHub : Hub` (clients call nothing; server pushes `JobUpdated`, `MessageReceived`, `LocationUpdated`, `Notification`, `Typing`, `Seen`); `SignalRBroadcaster : IBroadcaster` mapping those pushes onto `IHubContext<DemoHub>`; a running app whose startup does delete→create→seed against `Data Source=fixithere-demo.db` (or in-memory during tests); `Program` type exposed for `WebApplicationFactory<Program>`.

- [ ] **Step 1: Implement `src/Backend.Api/Hub.fs`**

```fsharp
module FixItHere.Backend.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR
open FixItHere.Shared.Dtos
open FixItHere.Backend.Services

type DemoHub() =
    inherit Hub()

type SignalRBroadcaster(ctx: IHubContext<DemoHub>) =
    interface IBroadcaster with
        member _.JobUpdated dto = ctx.Clients.All.SendAsync("JobUpdated", dto)
        member _.MessageReceived dto = ctx.Clients.All.SendAsync("MessageReceived", dto)
        member _.LocationUpdated dto = ctx.Clients.All.SendAsync("LocationUpdated", dto)
        member _.Notify text = ctx.Clients.All.SendAsync("Notification", text)
```

- [ ] **Step 2: Rewrite `src/Backend.Api/Program.fs`**

```fsharp
module FixItHere.Backend.Program

open Microsoft.AspNetCore.Builder
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open FixItHere.Backend.Db
open FixItHere.Backend.Services
open FixItHere.Backend.Hub

let builder = WebApplication.CreateBuilder()
builder.Services.AddDbContext<AppDb>(fun opts ->
    opts.UseSqlite("Data Source=fixithere-demo.db") |> ignore) |> ignore
builder.Services.AddSignalR() |> ignore
builder.Services.AddScoped<IBroadcaster, SignalRBroadcaster>() |> ignore
builder.Services.AddScoped<JobService>() |> ignore

let app = builder.Build()

// Every startup: drop, recreate, reseed — byte-identical demo data.
do
    use scope = app.Services.CreateScope()
    let db = scope.ServiceProvider.GetRequiredService<AppDb>()
    db.Database.EnsureDeleted() |> ignore
    db.Database.EnsureCreated() |> ignore
    FixItHere.Backend.Seed.run db

app.MapHub<DemoHub>("/hub") |> ignore
app.MapGet("/health", System.Func<string>(fun () -> "ok")) |> ignore

app.Run()

type Program() = class end   // marker for WebApplicationFactory
```

- [ ] **Step 3: Test factory + smoke test**

`tests/Backend.Api.Tests/AppFactory.fs` (compile before endpoint tests):

```fsharp
module FixItHere.Backend.Tests.AppFactory

open System.Net
open Microsoft.AspNetCore.Mvc.Testing
open Xunit
open FixItHere.Backend.Program

type Factory() =
    inherit WebApplicationFactory<Program>()

[<Fact>]
let ``app boots, seeds, and serves health`` () =
    use factory = new Factory()
    use client = factory.CreateClient()
    let resp = client.GetAsync("/health").Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
```

- [ ] **Step 4: Run — PASS, commit**

Run: `dotnet test tests/Backend.Api.Tests` — Expected: PASS (boot performs delete/create/seed against the file DB; acceptable for the demo).

```bash
git add -A && git commit -m "feat: SignalR DemoHub, app wiring, startup reset+reseed"
```

### Task 8: REST endpoints (login, catalog, jobs, messages, ratings, location, payment)

**Files:**
- Create: `src/Backend.Api/Endpoints.fs` (after Hub.fs, before Program.fs)
- Modify: `src/Backend.Api/Program.fs` (add `Endpoints.mapAll app`)
- Test: `tests/Backend.Api.Tests/EndpointTests.fs`

**Interfaces:**
- Consumes: `JobService`, `toJobDto`, `Envelope`, `JobStateCodec`, DTOs from Task 3.
- Produces: `module FixItHere.Backend.Endpoints` with `mapAll : WebApplication -> unit` implementing:
  `POST /login`, `GET /services`, `GET /providers?serviceId=&lat=&lng=` (haversine-sorted), `GET /providers/{id}`, `POST /jobs`, `GET /jobs?customerId=|providerId=`, `GET /jobs/{id}`, `PUT /jobs/{id}/accept|enroute|arrive|start|complete|cancel`, `GET /messages?jobId=`, `POST /messages`, `GET /ratings?providerId=`, `POST /ratings` (rating a `Completed` job also applies `RateAndClose`), `GET /location?providerId=`, `PUT /location`, `POST /payment/simulate`.
- All responses `Envelope`-wrapped; invalid transitions → HTTP 409; not-found → 404.

- [ ] **Step 1: Failing endpoint tests**

`tests/Backend.Api.Tests/EndpointTests.fs`:

```fsharp
module FixItHere.Backend.Tests.EndpointTests

open System.Net
open System.Net.Http.Json
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory

let client () = (new Factory()).CreateClient()

[<Fact>]
let ``login returns fake token for named customer`` () =
    use c = client ()
    let resp = c.PostAsJsonAsync("/login", { Role = "Customer"; Name = "John" }).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<LoginResponse>>().Result
    Assert.True(env.Success)
    Assert.Equal("Customer", env.Data.Role)
    Assert.StartsWith("fake-customer-", env.Data.Token)

[<Fact>]
let ``services returns the seven catalog services`` () =
    use c = client ()
    let env = c.GetFromJsonAsync<Envelope<ServiceDto list>>("/services").Result
    Assert.Equal(7, List.length env.Data)

[<Fact>]
let ``providers are sorted by proximity to query point`` () =
    use c = client ()
    let env =
        c.GetFromJsonAsync<Envelope<ProviderDto list>>(
            "/providers?lat=43.65&lng=-79.38").Result
    let dist (p: ProviderDto) =
        let dLat = p.Lat - 43.65
        let dLng = p.Lng - (-79.38)
        dLat * dLat + dLng * dLng
    let ds = env.Data |> List.map dist
    Assert.Equal<float list>(List.sort ds, ds)

[<Fact>]
let ``full job lifecycle over http`` () =
    use c = client ()
    let created =
        c.PostAsJsonAsync("/jobs",
            { CustomerId = 1; ProviderId = 1; ServiceId = 1
              ScheduleChoice = "Now"; Lat = 43.65; Lng = -79.38
              Address = "1 Yonge St" }).Result
    let job = created.Content.ReadFromJsonAsync<Envelope<JobDto>>().Result.Data
    let put (path: string) =
        c.PutAsync(sprintf "/jobs/%d/%s" job.Id path, null).Result
    Assert.Equal(HttpStatusCode.OK, (put "accept").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "enroute").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "arrive").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "start").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "complete").StatusCode)
    // invalid: complete again → 409
    Assert.Equal(HttpStatusCode.Conflict, (put "complete").StatusCode)

[<Fact>]
let ``payment simulate returns transferred amount`` () =
    use c = client ()
    let resp = c.PostAsJsonAsync("/payment/simulate", { JobId = 1 }).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<PaymentResult>>().Result
    Assert.Equal("Transferred", env.Data.Status)
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 3: Implement `src/Backend.Api/Endpoints.fs`**

```fsharp
module FixItHere.Backend.Endpoints

open System
open System.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

let private okJson (data: 't) = Results.Json(Envelope.ok data)
let private err (status: int) (msg: string) =
    Results.Json(Envelope.fail msg, statusCode = status)

let private haversineKm (lat1, lng1) (lat2, lng2) =
    let rad d = d * Math.PI / 180.0
    let dLat = rad (lat2 - lat1)
    let dLng = rad (lng2 - lng1)
    let a =
        sin (dLat / 2.0) ** 2.0
        + cos (rad lat1) * cos (rad lat2) * sin (dLng / 2.0) ** 2.0
    6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))

let private toProviderDto (db: AppDb) (p: Provider) : ProviderDto =
    let svcName = db.Services.Single(fun s -> s.Id = p.ServiceId).Name
    let ratings = db.Ratings.Where(fun r -> r.RateeId = p.Id).Select(fun r -> r.Stars).ToList()
    { Id = p.Id; BusinessName = p.BusinessName
      ServiceId = p.ServiceId; ServiceName = svcName
      Rating = (if ratings.Count = 0 then 0.0 else ratings |> Seq.averageBy float)
      RatingCount = ratings.Count
      Lat = p.Lat; Lng = p.Lng; Online = p.Online
      Vehicle = p.Vehicle; PhotoUrl = p.PhotoUrl }

let mapAll (app: WebApplication) =

    app.MapPost("/login", Func<LoginRequest, AppDb, IResult>(fun req db ->
        match req.Role with
        | "Customer" ->
            match db.Customers.SingleOrDefault(fun c -> c.Name = req.Name) |> Option.ofObj with
            | Some c ->
                okJson { Token = sprintf "fake-customer-%d" c.Id
                         UserId = c.Id; Role = "Customer"; DisplayName = c.Name }
            | None -> err 404 (sprintf "No customer named %s" req.Name)
        | "Provider" ->
            match db.Providers.SingleOrDefault(fun p -> p.BusinessName = req.Name) |> Option.ofObj with
            | Some p ->
                okJson { Token = sprintf "fake-provider-%d" p.Id
                         UserId = p.Id; Role = "Provider"; DisplayName = p.BusinessName }
            | None -> err 404 (sprintf "No provider named %s" req.Name)
        | r -> err 400 (sprintf "Unknown role %s" r))) |> ignore

    app.MapGet("/services", Func<AppDb, IResult>(fun db ->
        okJson (db.Services.OrderBy(fun s -> s.Id)
                |> Seq.map (fun s -> { Id = s.Id; Name = s.Name })
                |> List.ofSeq))) |> ignore

    app.MapGet("/providers", Func<AppDb, Nullable<int>, Nullable<float>, Nullable<float>, IResult>(
        fun db serviceId lat lng ->
            let q =
                if serviceId.HasValue
                then db.Providers.Where(fun p -> p.ServiceId = serviceId.Value)
                else db.Providers.AsQueryable()
            let dtos = q |> Seq.map (toProviderDto db) |> List.ofSeq
            let sorted =
                if lat.HasValue && lng.HasValue then
                    dtos |> List.sortBy (fun p -> haversineKm (lat.Value, lng.Value) (p.Lat, p.Lng))
                else dtos
            okJson sorted)) |> ignore

    app.MapGet("/providers/{id}", Func<AppDb, int, IResult>(fun db id ->
        match db.Providers.SingleOrDefault(fun p -> p.Id = id) |> Option.ofObj with
        | Some p -> okJson (toProviderDto db p)
        | None -> err 404 (sprintf "Provider %d not found" id))) |> ignore

    app.MapPost("/jobs", Func<CreateJobRequest, JobService, System.Threading.Tasks.Task<IResult>>(
        fun req svc -> task {
            let! dto = svc.Create req
            return okJson dto })) |> ignore

    app.MapGet("/jobs", Func<AppDb, Nullable<int>, Nullable<int>, IResult>(
        fun db customerId providerId ->
            let q =
                if customerId.HasValue then db.Jobs.Where(fun j -> j.CustomerId = customerId.Value)
                elif providerId.HasValue then db.Jobs.Where(fun j -> j.ProviderId = providerId.Value)
                else db.Jobs.AsQueryable()
            okJson (q |> Seq.map (toJobDto db) |> List.ofSeq))) |> ignore

    app.MapGet("/jobs/{id}", Func<AppDb, int, IResult>(fun db id ->
        match db.Jobs.SingleOrDefault(fun j -> j.Id = id) |> Option.ofObj with
        | Some j -> okJson (toJobDto db j)
        | None -> err 404 (sprintf "Job %d not found" id))) |> ignore

    let mapTransition (path: string) (event: JobEvent) =
        app.MapPut(sprintf "/jobs/{id}/%s" path,
            Func<int, JobService, System.Threading.Tasks.Task<IResult>>(fun id svc -> task {
                match! svc.Apply id event with
                | Ok dto -> return okJson dto
                | Error msg when msg.Contains "not found" -> return err 404 msg
                | Error msg -> return err 409 msg })) |> ignore
    mapTransition "accept"   Accepted
    mapTransition "enroute"  DepartEnRoute
    mapTransition "arrive"   Arrive
    mapTransition "start"    StartWork
    mapTransition "complete" CompleteWork
    mapTransition "cancel"   Cancel

    app.MapGet("/messages", Func<AppDb, int, IResult>(fun db jobId ->
        okJson (db.Messages.Where(fun m -> m.JobId = jobId).OrderBy(fun m -> m.Id)
                |> Seq.map (fun m ->
                    let sender =
                        db.Customers.SingleOrDefault(fun c -> c.Id = m.SenderId) |> Option.ofObj
                        |> Option.map (fun c -> c.Name)
                        |> Option.defaultWith (fun () ->
                            db.Providers.SingleOrDefault(fun p -> p.Id = m.SenderId) |> Option.ofObj
                            |> Option.map (fun p -> p.BusinessName)
                            |> Option.defaultValue "Unknown")
                    { Id = m.Id; JobId = m.JobId; SenderId = m.SenderId; SenderName = sender
                      Text = m.Text; PhotoBase64 = m.PhotoBase64; SentAt = m.SentAt; Seen = m.Seen })
                |> List.ofSeq))) |> ignore

    app.MapPost("/messages", Func<SendMessageRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            let msg =
                { Id = 0; JobId = req.JobId; SenderId = req.SenderId
                  Text = req.Text; PhotoBase64 = req.PhotoBase64
                  SentAt = FixItHere.Backend.Seed.Epoch; Seen = false }
            db.Messages.Add msg |> ignore
            db.SaveChanges() |> ignore
            let saved = db.Messages.OrderByDescending(fun m -> m.Id).First()
            let dto =
                { Id = saved.Id; JobId = saved.JobId; SenderId = saved.SenderId
                  SenderName = ""; Text = saved.Text; PhotoBase64 = saved.PhotoBase64
                  SentAt = saved.SentAt; Seen = saved.Seen }
            do! hub.MessageReceived dto
            return okJson dto })) |> ignore

    app.MapGet("/ratings", Func<AppDb, int, IResult>(fun db providerId ->
        okJson (db.Ratings.Where(fun r -> r.RateeId = providerId)
                |> Seq.map (fun r ->
                    { Id = r.Id; JobId = r.JobId; RaterId = r.RaterId
                      RateeId = r.RateeId; Stars = r.Stars; Comment = r.Comment })
                |> List.ofSeq))) |> ignore

    app.MapPost("/ratings", Func<CreateRatingRequest, AppDb, JobService, System.Threading.Tasks.Task<IResult>>(
        fun req db svc -> task {
            let rating =
                { Id = 0; JobId = req.JobId; RaterId = req.RaterId
                  RateeId = req.RateeId; Stars = req.Stars; Comment = req.Comment }
            db.Ratings.Add rating |> ignore
            db.SaveChanges() |> ignore
            // Rating a completed job closes it (simplified single-sided close for the demo)
            let job = db.Jobs.SingleOrDefault(fun j -> j.Id = req.JobId)
            if not (obj.ReferenceEquals(job, null)) && job.State = "Completed" then
                let! _ = svc.Apply req.JobId RateAndClose
                ()
            let saved = db.Ratings.OrderByDescending(fun r -> r.Id).First()
            return okJson
                { Id = saved.Id; JobId = saved.JobId; RaterId = saved.RaterId
                  RateeId = saved.RateeId; Stars = saved.Stars; Comment = saved.Comment } })) |> ignore

    app.MapGet("/location", Func<AppDb, int, IResult>(fun db providerId ->
        match db.Providers.SingleOrDefault(fun p -> p.Id = providerId) |> Option.ofObj with
        | Some p ->
            okJson { ProviderId = p.Id; Lat = p.Lat; Lng = p.Lng
                     UpdatedAt = FixItHere.Backend.Seed.Epoch }
        | None -> err 404 (sprintf "Provider %d not found" providerId))) |> ignore

    app.MapPut("/location", Func<UpdateLocationRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            match db.Providers.SingleOrDefault(fun p -> p.Id = req.ProviderId) |> Option.ofObj with
            | None -> return err 404 (sprintf "Provider %d not found" req.ProviderId)
            | Some prov ->
                let updated = { prov with Lat = req.Lat; Lng = req.Lng }
                db.Entry(prov).CurrentValues.SetValues(updated)
                db.SaveChanges() |> ignore
                let dto = { ProviderId = prov.Id; Lat = req.Lat; Lng = req.Lng
                            UpdatedAt = FixItHere.Backend.Seed.Epoch }
                do! hub.LocationUpdated dto
                return okJson dto })) |> ignore

    app.MapPost("/payment/simulate", Func<PaymentRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = req.JobId) |> Option.ofObj with
            | None -> return err 404 (sprintf "Job %d not found" req.JobId)
            | Some job ->
                do! hub.Notify "Payment Complete"
                return okJson { JobId = job.Id; Amount = job.Price; Status = "Transferred" } })) |> ignore
```

In `Program.fs`, after `app.MapGet("/health", ...)`, add:

```fsharp
FixItHere.Backend.Endpoints.mapAll app
```

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: REST endpoints for login, catalog, jobs, chat, ratings, location, payment"
```

### Task 9: Dev endpoints + DemoOrchestrator

**Files:**
- Create: `src/Backend.Api/DevEndpoints.fs` (after Endpoints.fs)
- Modify: `src/Backend.Api/Program.fs`
- Test: `tests/Backend.Api.Tests/DevEndpointTests.fs`

**Interfaces:**
- Produces: `module FixItHere.Backend.DevEndpoints` with `mapAll : WebApplication -> unit`:
  - `POST /dev/reset` — delete/create/reseed, returns `okJson "reset"`.
  - `POST /dev/demo/start` — body `{ "customerId": int, "providerId": int }` (`[<CLIMutable>] type StartDemoRequest`); runs the scripted timeline in a background task; returns the created `JobDto` immediately.
  - Timeline (each step ~2s apart via `Task.Delay 2000`): create job → Notify "Provider Accepted" + accept → enroute → 5 interpolated `PUT`-equivalent location updates from provider position to job position → inject 2 chat messages → arrive → start → complete → Notify "Payment Complete" → post 5-star rating (closes job).
- Mapped in `Program.fs` **only** inside `if app.Environment.IsDevelopment() then ...`.

- [ ] **Step 1: Failing test**

`tests/Backend.Api.Tests/DevEndpointTests.fs` (WebApplicationFactory defaults to Development env, so dev routes are on):

```fsharp
module FixItHere.Backend.Tests.DevEndpointTests

open System.Net
open System.Net.Http.Json
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory

[<Fact>]
let ``dev reset responds ok`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.PostAsync("/dev/reset", null).Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

[<Fact>]
let ``demo start creates a scheduled job immediately`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.PostAsJsonAsync("/dev/demo/start", {| customerId = 1; providerId = 1 |}).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<JobDto>>().Result
    Assert.True(env.Success)
    Assert.Equal("Scheduled", env.Data.State)
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 3: Implement `src/Backend.Api/DevEndpoints.fs`**

```fsharp
module FixItHere.Backend.DevEndpoints

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

[<CLIMutable>] type StartDemoRequest = { CustomerId: int; ProviderId: int }

let private okJson (data: 't) = Results.Json(Envelope.ok data)

/// Scripted demo timeline. Runs on a background task with its own DI scope.
let private runTimeline (sp: IServiceProvider) (jobId: int) =
    task {
        use scope = sp.CreateScope()
        let db = scope.ServiceProvider.GetRequiredService<AppDb>()
        let hub = scope.ServiceProvider.GetRequiredService<IBroadcaster>()
        let svc = JobService(db, hub)
        let pause () = Task.Delay 2000
        let apply ev = task { let! _ = svc.Apply jobId ev in () }

        do! pause ()
        do! hub.Notify "Provider Accepted"
        do! apply Accepted
        do! pause ()
        do! apply DepartEnRoute
        // interpolate provider toward the job location in 5 steps
        let job = db.Jobs.AsNoTracking().Single(fun j -> j.Id = jobId)
        let prov = db.Providers.Single(fun p -> p.Id = job.ProviderId)
        let startLat, startLng = prov.Lat, prov.Lng
        for i in 1 .. 5 do
            do! pause ()
            let t = float i / 5.0
            let lat = startLat + (job.Lat - startLat) * t
            let lng = startLng + (job.Lng - startLng) * t
            let tracked = db.Providers.Single(fun p -> p.Id = job.ProviderId)
            db.Entry(tracked).CurrentValues.SetValues({ tracked with Lat = lat; Lng = lng })
            db.SaveChanges() |> ignore
            do! hub.LocationUpdated
                    { ProviderId = prov.Id; Lat = lat; Lng = lng
                      UpdatedAt = FixItHere.Backend.Seed.Epoch }
            if i = 2 then
                do! hub.MessageReceived
                        { Id = 0; JobId = jobId; SenderId = job.CustomerId; SenderName = "Customer"
                          Text = "Hi!"; PhotoBase64 = null
                          SentAt = FixItHere.Backend.Seed.Epoch; Seen = false }
            if i = 3 then
                do! hub.MessageReceived
                        { Id = 0; JobId = jobId; SenderId = job.ProviderId; SenderName = "Provider"
                          Text = "On my way."; PhotoBase64 = null
                          SentAt = FixItHere.Backend.Seed.Epoch; Seen = false }
        do! pause ()
        do! hub.Notify "Provider Arriving"
        do! apply Arrive
        do! pause ()
        do! apply StartWork
        do! pause ()
        do! apply CompleteWork
        do! pause ()
        do! hub.Notify "Payment Complete"
        db.Ratings.Add
            { Id = 0; JobId = jobId; RaterId = job.CustomerId; RateeId = job.ProviderId
              Stars = 5; Comment = "Great demo!" } |> ignore
        db.SaveChanges() |> ignore
        do! apply RateAndClose
    } :> Task

let mapAll (app: WebApplication) =
    app.MapPost("/dev/reset", Func<AppDb, IResult>(fun db ->
        db.Database.EnsureDeleted() |> ignore
        db.Database.EnsureCreated() |> ignore
        FixItHere.Backend.Seed.run db
        okJson "reset")) |> ignore

    app.MapPost("/dev/demo/start",
        Func<StartDemoRequest, JobService, AppDb, IServiceProvider, Task<IResult>>(
            fun req svc db sp -> task {
                let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
                let cust = db.Customers.Single(fun c -> c.Id = req.CustomerId)
                let! dto =
                    svc.Create
                        { CustomerId = cust.Id; ProviderId = prov.Id; ServiceId = prov.ServiceId
                          ScheduleChoice = "Now"; Lat = cust.Lat; Lng = cust.Lng
                          Address = "Demo location" }
                runTimeline sp dto.Id |> ignore   // fire-and-forget scripted timeline
                return okJson dto })) |> ignore
```

In `Program.fs`, replace the endpoint-mapping section with:

```fsharp
FixItHere.Backend.Endpoints.mapAll app
if app.Environment.IsDevelopment() then
    FixItHere.Backend.DevEndpoints.mapAll app
    app.UseStaticFiles() |> ignore   // serves wwwroot (dev console added in Task 10)
```

- [ ] **Step 4: Run — PASS, commit**

```bash
dotnet test tests/Backend.Api.Tests
git add -A && git commit -m "feat: dev endpoints with scripted Start Demo orchestrator"
```

### Task 10: `/dev` Demo Control Panel (static HTML/JS)

**Files:**
- Create: `src/Backend.Api/wwwroot/dev/index.html`
- Modify: `src/Backend.Api/Program.fs` (redirect `/dev` → `/dev/index.html` in Development)
- Test: `tests/Backend.Api.Tests/DevConsoleTests.fs`

**Interfaces:**
- Consumes: every endpoint from Tasks 8–9 plus `/hub` SignalR events (`JobUpdated`, `MessageReceived`, `LocationUpdated`, `Notification`).
- Produces: one self-contained HTML page (Leaflet + SignalR from CDN — acceptable for a dev tool) with: persona pickers, job list with per-job transition buttons, Leaflet map showing providers/customers (click map with a provider selected = reposition), message injector, payment simulate, **Reset Demo**, **Start Demo**, and a live event log fed by the hub.

- [ ] **Step 1: Failing test**

`tests/Backend.Api.Tests/DevConsoleTests.fs`:

```fsharp
module FixItHere.Backend.Tests.DevConsoleTests

open System.Net
open Xunit
open FixItHere.Backend.Tests.AppFactory

[<Fact>]
let ``dev console page is served in development`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.GetAsync("/dev/index.html").Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let body = resp.Content.ReadAsStringAsync().Result
    Assert.Contains("FixItHere Demo Control Panel", body)
```

- [ ] **Step 2: Run — FAIL.** `dotnet test tests/Backend.Api.Tests`

- [ ] **Step 3: Create `src/Backend.Api/wwwroot/dev/index.html`**

```html
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>FixItHere Demo Control Panel</title>
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<style>
  body { font-family: system-ui, sans-serif; margin: 0; display: grid;
         grid-template-columns: 340px 1fr 320px; height: 100vh; }
  .col { padding: 12px; overflow-y: auto; border-right: 1px solid #ddd; }
  h1 { font-size: 16px; margin: 0 0 12px; }
  h2 { font-size: 13px; text-transform: uppercase; color: #666; margin: 16px 0 6px; }
  button { margin: 2px; padding: 4px 10px; cursor: pointer; }
  #map { height: 100%; }
  .job { border: 1px solid #ccc; border-radius: 6px; padding: 6px; margin: 4px 0; font-size: 12px; }
  .job b { font-size: 13px; }
  #log { font-size: 11px; font-family: monospace; white-space: pre-wrap; }
  .big { font-size: 15px; padding: 8px 14px; font-weight: 600; }
  select, input { margin: 2px; padding: 4px; }
</style>
</head>
<body>
<div class="col">
  <h1>FixItHere Demo Control Panel</h1>
  <button class="big" onclick="startDemo()">▶ Start Demo</button>
  <button class="big" onclick="resetDemo()">↺ Reset Demo</button>
  <h2>Personas</h2>
  Customer <select id="customer"></select><br>
  Provider <select id="provider"></select>
  <h2>Create Job</h2>
  <button onclick="createJob()">Create job (customer → provider)</button>
  <h2>Inject Message</h2>
  <input id="msgJobId" placeholder="job id" size="6">
  <input id="msgText" placeholder="text" size="18">
  <button onclick="injectMsg(true)">as Customer</button>
  <button onclick="injectMsg(false)">as Provider</button>
  <h2>Payment</h2>
  <input id="payJobId" placeholder="job id" size="6">
  <button onclick="simulatePayment()">Force Payment</button>
  <h2>Jobs (non-terminal)</h2>
  <div id="jobs"></div>
</div>
<div id="map"></div>
<div class="col">
  <h2>Live Events</h2>
  <div id="log"></div>
</div>
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<script src="https://unpkg.com/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
<script>
const api = async (method, path, body) => {
  const resp = await fetch(path, {
    method, headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined });
  const env = await resp.json();
  if (!env.success) log(`ERR ${path}: ${env.error}`);
  return env.data;
};
const log = (msg) => {
  const el = document.getElementById("log");
  el.textContent = new Date().toLocaleTimeString() + "  " + msg + "\n" + el.textContent;
};

// --- map ---
const map = L.map("map").setView([43.7, -79.45], 10);
L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png",
  { attribution: "&copy; OpenStreetMap" }).addTo(map);
const providerMarkers = {};
map.on("click", async (e) => {
  const pid = parseInt(document.getElementById("provider").value);
  await api("PUT", "/location", { providerId: pid, lat: e.latlng.lat, lng: e.latlng.lng });
  log(`Moved provider ${pid} to ${e.latlng.lat.toFixed(4)},${e.latlng.lng.toFixed(4)}`);
});

// --- data loading ---
async function refresh() {
  const provs = await api("GET", "/providers");
  const provSel = document.getElementById("provider");
  provSel.innerHTML = provs.map(p =>
    `<option value="${p.id}">${p.businessName} (${p.serviceName})</option>`).join("");
  provs.forEach(p => {
    if (!providerMarkers[p.id])
      providerMarkers[p.id] = L.marker([p.lat, p.lng]).addTo(map)
        .bindPopup(p.businessName);
    else providerMarkers[p.id].setLatLng([p.lat, p.lng]);
  });
  // fake-login list uses seeded customers 1..20; named five are guaranteed present
  const custSel = document.getElementById("customer");
  custSel.innerHTML = ["John","Mary","Steve","Susan","Bob"].map((n, i) => "").join("");
  custSel.innerHTML = "";
  for (const name of ["John","Mary","Steve","Susan","Bob"]) {
    const u = await api("POST", "/login", { role: "Customer", name });
    custSel.innerHTML += `<option value="${u.userId}">${u.displayName}</option>`;
  }
  await refreshJobs();
}
async function refreshJobs() {
  const jobs = await api("GET", "/jobs");
  const active = jobs.filter(j => !["Closed","Cancelled"].includes(j.state));
  document.getElementById("jobs").innerHTML = active.map(j => `
    <div class="job"><b>#${j.id}</b> ${j.customerName} → ${j.providerName}
      [${j.serviceName}] <b>${j.state}</b> $${j.price}<br>
      ${["accept","enroute","arrive","start","complete","cancel"].map(t =>
        `<button onclick="transition(${j.id},'${t}')">${t}</button>`).join("")}
    </div>`).join("");
}

// --- actions ---
async function transition(id, t) {
  await api("PUT", `/jobs/${id}/${t}`);
  await refreshJobs();
}
async function createJob() {
  const customerId = parseInt(document.getElementById("customer").value);
  const providerId = parseInt(document.getElementById("provider").value);
  const provs = await api("GET", "/providers");
  const p = provs.find(x => x.id === providerId);
  await api("POST", "/jobs", { customerId, providerId, serviceId: p.serviceId,
    scheduleChoice: "Now", lat: 43.65, lng: -79.38, address: "1 Demo St" });
  await refreshJobs();
}
async function injectMsg(asCustomer) {
  const jobId = parseInt(document.getElementById("msgJobId").value);
  const job = await api("GET", `/jobs/${jobId}`);
  await api("POST", "/messages", { jobId,
    senderId: asCustomer ? job.customerId : job.providerId,
    text: document.getElementById("msgText").value, photoBase64: null });
}
async function simulatePayment() {
  const jobId = parseInt(document.getElementById("payJobId").value);
  const r = await api("POST", "/payment/simulate", { jobId });
  log(`Payment: $${r.amount} ${r.status}`);
}
async function startDemo() {
  const customerId = parseInt(document.getElementById("customer").value);
  const providerId = parseInt(document.getElementById("provider").value);
  const job = await api("POST", "/dev/demo/start", { customerId, providerId });
  log(`Demo started with job #${job.id}`);
}
async function resetDemo() {
  await api("POST", "/dev/reset");
  log("Demo reset");
  await refresh();
}

// --- realtime ---
const conn = new signalR.HubConnectionBuilder().withUrl("/hub").build();
conn.on("JobUpdated", j => { log(`Job #${j.id} → ${j.state}`); refreshJobs(); });
conn.on("MessageReceived", m => log(`Msg [job ${m.jobId}] ${m.senderName}: ${m.text}`));
conn.on("LocationUpdated", l => {
  log(`Provider ${l.providerId} @ ${l.lat.toFixed(4)},${l.lng.toFixed(4)}`);
  if (providerMarkers[l.providerId]) providerMarkers[l.providerId].setLatLng([l.lat, l.lng]);
});
conn.on("Notification", t => log(`🔔 ${t}`));
conn.start().then(() => log("SignalR connected"));

refresh();
</script>
</body>
</html>
```

In `Program.fs`, inside the `IsDevelopment` block, add before `UseStaticFiles`:

```fsharp
app.MapGet("/dev", Func<IResult>(fun () -> Results.Redirect "/dev/index.html")) |> ignore
```

(The fsproj for a `dotnet new web` template includes `wwwroot` content automatically; if not, add `<Content Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />`.)

- [ ] **Step 4: Run tests — PASS, then manual smoke**

```bash
dotnet test
dotnet run --project src/Backend.Api
```

Open `http://localhost:5000/dev` (or the printed port). Verify: map renders with 20 provider markers; personas load; **Create job** adds a card; transition buttons walk the lifecycle and the event log streams `JobUpdated`; clicking the map moves the selected provider's marker live; **Start Demo** plays the whole timeline hands-free; **Reset Demo** restores seed state.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: /dev demo control panel with live map and event log"
```

### Task 11: Full-suite verification + docs

**Files:**
- Create: `README.md`
- Modify: none

- [ ] **Step 1: Run everything**

```bash
dotnet build && dotnet test
```

Expected: build clean, all tests green (Shared.Tests + Backend.Api.Tests).

- [ ] **Step 2: Write `README.md`**

```markdown
# FixItHere.Demo

Proof-of-concept for the FixItHere mobile-services marketplace.
See `docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`.

## Run the backend + demo control panel

    dotnet run --project src/Backend.Api

Then open http://localhost:5000/dev — press **Start Demo** to watch the full
book → accept → travel → chat → arrive → work → pay → rate flow, live.

Every startup resets the database to identical seed data
(20 customers, 20 providers, 80 jobs).

## Test

    dotnet test

## Projects

- `src/Shared` — pure F# domain: types, DTOs, Job state machine
- `src/Backend.Api` — F# Minimal API + EF Core/SQLite + SignalR + /dev console
- `src/Customer.Mobile`, `src/Provider.Mobile` — Fabulous MAUI apps (Plans 2–3)
```

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "docs: README with run and test instructions"
```

---

## Self-review notes

- **Spec coverage (Plan 1 scope):** solution scaffold ✔ (T1), state machine ✔ (T2), DTOs/envelope ✔ (T3), EF/SQLite ✔ (T4), deterministic seed with counts + named personas + 7 services ✔ (T5), JobService + broadcast ✔ (T6), DemoHub + startup reset ✔ (T7), all ~18 endpoints incl. haversine sort, 409-on-invalid, fake JWT ✔ (T8), dev endpoints + Start Demo orchestrator ✔ (T9), /dev console with map/persona/inject/reset ✔ (T10), README ✔ (T11). Mobile apps, simulated GPS UI, fake calls, auto-reply: deferred to Plans 2–3 by design. `Typing`/`Seen` hub events are declared in the contract but only exercised by the apps (Plans 2–3) — the hub sends what services invoke; no dead code added now.
- **Type consistency check:** `Envelope.ok/fail`, `JobService.Apply/Create`, `IBroadcaster` member names, `JobStateCodec.ofState/toState`, DTO field names match across Tasks 3–10.
- **Known simplifications (deliberate):** payment simulate returns `Transferred` in one call (two-phase animation happens client-side); rating closes a `Completed` job single-sidedly; startup file-DB reset also runs under tests (harmless: each factory boot reseeds).
```
