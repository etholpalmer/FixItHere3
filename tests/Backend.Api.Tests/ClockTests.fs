module FixItHere.Backend.Tests.ClockTests

open System
open System.Net
open System.Net.Http.Json
open System.Threading

open Xunit

open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory
open System.Linq

let private client () = (new Factory()).CreateClient()

let private clock (c: Net.Http.HttpClient) =
    c.GetFromJsonAsync<Envelope<DemoClockDto>>("/demo/clock").Result.Data

let private post (c: Net.Http.HttpClient) action rate target =
    c.PostAsJsonAsync("/demo/clock", { Action = action; Rate = rate; Target = target }).Result

let private demoNow (d: DemoClockDto) =
    DateTimeOffset.Parse(d.DemoNow, Globalization.CultureInfo.InvariantCulture,
                         Globalization.DateTimeStyles.RoundtripKind)

[<Fact>]
let ``the clock starts at the epoch, running, at real time`` () =
    // Starting at the seed's epoch is what puts all thirty seeded Scheduled
    // jobs legitimately in the future without the seed reading a wall clock.
    use c = client ()
    let d = clock c
    Assert.True d.Running
    Assert.Equal(1.0, d.Rate)
    Assert.True((demoNow d - DemoClock.epoch).Duration() < TimeSpan.FromSeconds 5.0)

[<Fact>]
let ``pausing freezes demo time and resuming continues it`` () =
    use c = client ()
    let paused = (post c "pause" 0.0 "").Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data
    Assert.False paused.Running
    Thread.Sleep 250
    // Read twice across real time: a paused clock must report the same instant.
    Assert.Equal(demoNow paused, demoNow (clock c))
    let resumed = (post c "resume" 0.0 "").Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data
    Assert.True resumed.Running

[<Fact>]
let ``rate is applied and clamped to the supported band`` () =
    use c = client ()
    Assert.Equal(60.0, (post c "rate" 60.0 "").Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data.Rate)
    // Past the cap the provider's car teleports between location pushes instead
    // of gliding, so the ceiling is a demo-quality limit, not a safety rail.
    Assert.Equal(DemoClock.maxRate, (post c "rate" 100000.0 "").Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data.Rate)
    Assert.Equal(DemoClock.minRate, (post c "rate" -4.0 "").Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data.Rate)

[<Fact>]
let ``jumping while paused resumes, so the countdown never sticks`` () =
    use c = client ()
    post c "pause" 0.0 "" |> ignore
    let target = DemoClock.epoch.AddHours 5.0
    let jumped = (post c "jump" 0.0 (target.ToString "o")).Content.ReadFromJsonAsync<Envelope<DemoClockDto>>().Result.Data
    Assert.True jumped.Running
    Assert.True((demoNow jumped - target).Duration() < TimeSpan.FromSeconds 5.0)

[<Fact>]
let ``an unknown action or unparseable target is rejected, not ignored`` () =
    // A silent no-op leaves the console's buttons looking dead, which during a
    // demo is indistinguishable from the backend having fallen over.
    use c = client ()
    Assert.Equal(HttpStatusCode.BadRequest, (post c "warp" 0.0 "").StatusCode)
    Assert.Equal(HttpStatusCode.BadRequest, (post c "jump" 0.0 "next tuesday").StatusCode)

[<Fact>]
let ``resetting the world resets the clock with it`` () =
    // The coupling that persisting the anchor would have broken: after a reseed
    // every job is re-anchored at the epoch, so a clock left hours ahead would
    // render the entire freshly-reset list instantly overdue.
    use c = client ()
    post c "jump" 0.0 ((DemoClock.epoch.AddDays 3.0).ToString "o") |> ignore
    Assert.True((demoNow (clock c) - DemoClock.epoch) > TimeSpan.FromDays 2.0)
    c.PostAsync("/dev/reset", null).Result |> ignore
    let after = clock c
    Assert.True after.Running
    Assert.Equal(1.0, after.Rate)
    Assert.True((demoNow after - DemoClock.epoch).Duration() < TimeSpan.FromSeconds 5.0)

// --------------------------------------------------------- Reschedule I/O ----

[<Fact>]
let ``the reschedule sub-status survives a round trip through the job columns`` () =
    // The pure function is tested in Shared. This is the other half: that what
    // the columns give back is what the domain type put in.
    let db, conn = FixItHere.Backend.Tests.DbTests.makeDb ()
    use _ = conn
    FixItHere.Backend.Seed.run db
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")

    let loaded = FixItHere.Backend.Services.readReschedule job
    Assert.Equal(DateTimeOffset.Parse job.PromisedStart, loaded.PromisedStart)
    Assert.True loaded.Pending.IsNone

    let proposal =
        { ProposedStart = loaded.PromisedStart.AddMinutes 15.0
          By = ActorRole.Provider
          Reason = "Traffic on the DVP"
          ExpiresAt = loaded.PromisedStart }
    let withPending = FixItHere.Backend.Services.writeReschedule job { loaded with Pending = Some proposal }
    match (FixItHere.Backend.Services.readReschedule withPending).Pending with
    | Some back -> Assert.Equal(proposal, back)
    | None -> failwith "a written proposal did not read back"

    // ...and clearing it really clears every column, rather than leaving a
    // half-populated proposal the UI would render as live.
    let cleared = FixItHere.Backend.Services.writeReschedule withPending { loaded with Pending = None }
    Assert.Equal("", cleared.ProposedStart)
    Assert.Equal("", cleared.ProposedBy)
    Assert.Equal("", cleared.ProposalReason)
    Assert.Equal("", cleared.ProposalExpiresAt)
    Assert.True (FixItHere.Backend.Services.readReschedule cleared).Pending.IsNone

[<Fact>]
let ``a job that has already been worked cannot be rescheduled`` () =
    let db, conn = FixItHere.Backend.Tests.DbTests.makeDb ()
    use _ = conn
    FixItHere.Backend.Seed.run db
    let svc = FixItHere.Backend.Services.JobService(db, FixItHere.Backend.Services.NullBroadcaster())
    let closed = db.Jobs.First(fun j -> j.State = "Closed")
    let proposal =
        { ProposedStart = DemoClock.epoch.AddHours 3.0
          By = ActorRole.Provider; Reason = "late"
          ExpiresAt = DemoClock.epoch.AddHours 1.0 }
    let result = (svc.Reschedule closed.Id DemoClock.epoch (Propose proposal)).Result
    Assert.True(Result.isError result)

[<Fact>]
let ``arriving settles a pending proposal`` () =
    // Found by watching the console rather than by reading code: the scripted
    // late demo left "Provider proposed 26:23 late" on a job whose work was
    // already InProgress, because nothing cleared the proposal when the
    // provider actually turned up.
    let db, conn = FixItHere.Backend.Tests.DbTests.makeDb ()
    use _ = conn
    FixItHere.Backend.Seed.run db
    let svc = FixItHere.Backend.Services.JobService(db, FixItHere.Backend.Services.NullBroadcaster())
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")
    let now = DateTimeOffset.Parse job.PromisedStart |> fun p -> p.AddHours -1.0
    let proposal =
        { ProposedStart = DateTimeOffset.Parse(job.PromisedStart).AddMinutes 15.0
          By = ActorRole.Provider; Reason = "Traffic on the DVP"
          ExpiresAt = now + Reschedule.proposalWindow }
    Assert.True((svc.Reschedule job.Id now (Propose proposal)).Result |> Result.isOk)
    Assert.True(db.Jobs.Single(fun j -> j.Id = job.Id).ProposedStart <> "")

    // En route it still stands — they have not arrived yet.
    (svc.Apply job.Id DepartEnRoute).Result |> ignore
    Assert.True(db.Jobs.Single(fun j -> j.Id = job.Id).ProposedStart <> "")

    (svc.Apply job.Id Arrive).Result |> ignore
    Assert.Equal("", db.Jobs.Single(fun j -> j.Id = job.Id).ProposedStart)

// -------------------------------------------------- Escalation endpoints ----

let private soonestScheduled (c: Net.Http.HttpClient) =
    c.GetFromJsonAsync<Envelope<JobDto[]>>("/jobs?customerId=1").Result.Data
    |> Array.filter (fun j -> j.State = "Scheduled")
    |> Array.minBy (fun j -> j.PromisedStart)

let private propose (c: Net.Http.HttpClient) jobId role (at: DateTimeOffset) =
    c.PostAsJsonAsync("/jobs/reschedule",
        { JobId = jobId; ByRole = role; ProposedStart = at.ToString "o"; Reason = "Traffic on the DVP" }).Result

let private decide (c: Net.Http.HttpClient) jobId role accept =
    c.PostAsJsonAsync("/jobs/reschedule/decision",
        { JobId = jobId; ByRole = role; Accept = accept }).Result

[<Fact>]
let ``a no-show cannot be reported before the grace window, and the error says when`` () =
    // The gate is the clock, not the visibility of a button. A UI that shows
    // the control early must still be refused.
    use c = client ()
    let job = soonestScheduled c
    let resp = c.PostAsJsonAsync("/jobs/no-show", { JobId = job.Id; ByRole = "Customer" }).Result
    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode)
    Assert.Contains("Too early", resp.Content.ReadAsStringAsync().Result)

[<Fact>]
let ``declining leaves the promise standing and clears the proposal`` () =
    use c = client ()
    let job = soonestScheduled c
    let original = DateTimeOffset.Parse job.PromisedStart
    Assert.Equal(HttpStatusCode.OK, (propose c job.Id "Provider" (original.AddMinutes 15.0)).StatusCode)

    // The proposer answering itself would make this unilateral.
    Assert.Equal(HttpStatusCode.Conflict, (decide c job.Id "Provider" true).StatusCode)

    let declined = (decide c job.Id "Customer" false).Content.ReadFromJsonAsync<Envelope<JobDto>>().Result.Data
    Assert.Equal(original, DateTimeOffset.Parse declined.PromisedStart)
    Assert.Equal("", declined.ProposedStart)

[<Fact>]
let ``accepting moves the promise and pushes the no-show deadline with it`` () =
    use c = client ()
    let job = soonestScheduled c
    let original = DateTimeOffset.Parse job.PromisedStart
    propose c job.Id "Provider" (original.AddMinutes 15.0) |> ignore
    let agreed = (decide c job.Id "Customer" true).Content.ReadFromJsonAsync<Envelope<JobDto>>().Result.Data
    Assert.Equal(original.AddMinutes 15.0, DateTimeOffset.Parse agreed.PromisedStart)
    Assert.Equal("", agreed.ProposedStart)

    // Past the *original* deadline but not the new one: still too early.
    post c "jump" 0.0 ((original.AddMinutes Reschedule.graceMinutes).AddSeconds 1.0 |> fun d -> d.ToString "o") |> ignore
    Assert.Equal(HttpStatusCode.Conflict,
                 c.PostAsJsonAsync("/jobs/no-show", { JobId = job.Id; ByRole = "Customer" }).Result.StatusCode)

[<Fact>]
let ``past the grace window the job becomes a no-show, once`` () =
    use c = client ()
    let job = soonestScheduled c
    let deadline = DateTimeOffset.Parse(job.PromisedStart).AddMinutes(Reschedule.graceMinutes + 1.0)
    post c "jump" 0.0 (deadline.ToString "o") |> ignore

    let resp = c.PostAsJsonAsync("/jobs/no-show", { JobId = job.Id; ByRole = "Customer" }).Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    Assert.Equal("ProviderNoShow", resp.Content.ReadFromJsonAsync<Envelope<JobDto>>().Result.Data.State)

    // Terminal. A second report is the state machine's job to refuse.
    Assert.Equal(HttpStatusCode.Conflict,
                 c.PostAsJsonAsync("/jobs/no-show", { JobId = job.Id; ByRole = "Customer" }).Result.StatusCode)

[<Fact>]
let ``an unknown role is rejected rather than guessed`` () =
    use c = client ()
    let job = soonestScheduled c
    Assert.Equal(HttpStatusCode.BadRequest,
                 (propose c job.Id "Admin" (DateTimeOffset.Parse(job.PromisedStart).AddMinutes 15.0)).StatusCode)
    Assert.Equal(HttpStatusCode.BadRequest, (decide c job.Id "" true).StatusCode)
