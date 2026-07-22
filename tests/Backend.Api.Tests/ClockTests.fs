module FixItHere.Backend.Tests.ClockTests

open System
open System.Net
open System.Net.Http.Json
open System.Threading

open Xunit

open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory

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
