/// Tests for the Phase 2 contracts — `Actor`, `DemoClock` and `Reschedule`.
///
/// These three modules are pure, which is the reason they were committed ahead
/// of any consumer: every rule the two apps and the backend must agree on is
/// asserted here once, rather than re-implemented (and re-argued) three times.
module FixItHere.Shared.Tests.ContractTests

open System

open FsCheck.Xunit
open Xunit

open FixItHere.Shared

// ---------------------------------------------------------------- Actor ----

[<Fact>]
let ``customer 1 and provider 1 are different actors`` () =
    // The colliding shape, and the documented demo pair. Four separate bugs in
    // this codebase were this assertion failing in production.
    Assert.NotEqual(Actor.customer 1, Actor.provider 1)
    Assert.False(Actor.isWire (Actor.customer 1) 1 "Provider")
    Assert.True(Actor.isWire (Actor.customer 1) 1 "Customer")

[<Fact>]
let ``an unrecognised role never matches anyone`` () =
    // Under-matching is the safe direction: defaulting an unknown role to
    // Customer is exactly how a provider's id silently becomes a customer's.
    Assert.False(Actor.isWire (Actor.customer 1) 1 "customer")
    Assert.False(Actor.isWire (Actor.customer 1) 1 "")
    Assert.False(Actor.isWire (Actor.provider 1) 1 "Admin")
    Assert.True(Actor.ofWire 1 "nonsense" |> Option.isNone)

[<Property>]
let ``role survives a round trip through the wire`` (id: int) =
    [ ActorRole.Customer; ActorRole.Provider ]
    |> List.forall (fun role ->
        let a = { Role = role; Id = id }
        let wireId, wireRole = Actor.toWire a
        Actor.ofWire wireId wireRole = Some a)

// ------------------------------------------------------------ DemoClock ----

let private t (offsetSeconds: float) = DemoClock.epoch.AddSeconds offsetSeconds

[<Fact>]
let ``a running clock advances at its rate`` () =
    let c = { DemoClock.start (t 0.) with Rate = 60.0 }
    // Ten real seconds at 60x is ten demo minutes.
    Assert.Equal(DemoClock.epoch.AddMinutes 10.0, DemoClock.nowAt c (t 10.))

[<Fact>]
let ``a paused clock does not advance`` () =
    let paused = DemoClock.start (t 0.) |> DemoClock.pause (t 5.)
    Assert.Equal(DemoClock.nowAt paused (t 5.), DemoClock.nowAt paused (t 500.))

[<Property>]
let ``every clock mutation is continuous at the moment it is applied`` (rateSeed: int) (atSeed: int) =
    // The property the whole design rests on. If any mutation moved demo time
    // at the instant it was applied, every countdown on both phones would jump
    // — which is precisely what a re-anchored timer looks like to an audience.
    let at = t (float (abs atSeed % 3600))
    let rate = 1.0 + float (abs rateSeed % 119)
    let c = { DemoClock.start (t 0.) with Rate = 7.0 }
    let before = DemoClock.nowAt c at
    [ DemoClock.pause at c; DemoClock.resume at c; DemoClock.withRate rate at c ]
    |> List.forall (fun after -> DemoClock.nowAt after at = before)

[<Fact>]
let ``changing rate does not retroactively rescale elapsed time`` () =
    // Naively assigning Rate without re-anchoring rescales all history: the
    // clock below would read epoch+600s instead of epoch+70s.
    let c = DemoClock.start (t 0.) |> DemoClock.withRate 60.0 (t 10.)
    Assert.Equal(DemoClock.epoch.AddSeconds 10.0, DemoClock.nowAt c (t 10.))
    Assert.Equal(DemoClock.epoch.AddSeconds 70.0, DemoClock.nowAt c (t 11.))

[<Fact>]
let ``jumping always resumes`` () =
    // Shipping both behaviours was the failure mode the plan called out: a jump
    // against a paused clock freezes the countdown and reads as a hung app.
    let paused = DemoClock.start (t 0.) |> DemoClock.pause (t 1.)
    let jumped = paused |> DemoClock.jumpTo (DemoClock.epoch.AddHours 3.0) (t 1.)
    Assert.True jumped.Running
    Assert.Equal(DemoClock.epoch.AddHours 3.0, DemoClock.nowAt jumped (t 1.))

[<Fact>]
let ``demo time is clamped forward to the epoch`` () =
    let c = DemoClock.start (t 0.) |> DemoClock.jumpTo (DemoClock.epoch.AddDays -5.0) (t 1.)
    Assert.Equal(DemoClock.epoch, DemoClock.nowAt c (t 1.))

[<Property>]
let ``rate is always within bounds`` (r: float) =
    let clamped = DemoClock.clampRate r
    clamped >= DemoClock.minRate && clamped <= DemoClock.maxRate

// ------------------------------------------------------------ Reschedule ----

let private now = DemoClock.epoch.AddHours 1.0
let private promised = DemoClock.epoch.AddHours 2.0
let private booked = Reschedule.ofBooking promised

/// Unwrap or fail loudly. `let (Ok x) = ...` warns (FS0025) and silently
/// binds nothing useful on Error; this reports which call actually failed.
let private mustOk =
    function
    | Ok v -> v
    | Error (e: string) -> failwithf "expected Ok, got Error \"%s\"" e

let private proposalBy role minutesLater =
    { ProposedStart = promised.AddMinutes(float minutesLater)
      By = role
      Reason = "Traffic on the DVP"
      ExpiresAt = now + Reschedule.proposalWindow }

[<Fact>]
let ``a proposal can be raised, and only one at a time`` () =
    let p = proposalBy ActorRole.Provider 15
    match Reschedule.apply now booked (Propose p) with
    | Ok (r, ProposalRaised raised) ->
        Assert.Equal(p, raised)
        // The promise does not move until someone accepts.
        Assert.Equal(promised, r.PromisedStart)
        Assert.True(Reschedule.apply now r (Propose (proposalBy ActorRole.Customer 30)) |> Result.isError)
    | other -> failwithf "expected a raised proposal, got %A" other

[<Fact>]
let ``a proposal cannot promise the past or restate the present`` () =
    let intoThePast = { proposalBy ActorRole.Provider 0 with ProposedStart = now.AddMinutes -1.0 }
    Assert.True(Reschedule.apply now booked (Propose intoThePast) |> Result.isError)
    // Zero minutes later == the time already agreed.
    Assert.True(Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 0)) |> Result.isError)

[<Fact>]
let ``the proposing party cannot answer its own proposal`` () =
    // Without this guard a bilateral negotiation is a unilateral edit with
    // extra steps.
    let pending, _ = mustOk (Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 15)))
    Assert.True(Reschedule.apply now pending (AcceptProposal ActorRole.Provider) |> Result.isError)
    Assert.True(Reschedule.apply now pending (DeclineProposal ActorRole.Provider) |> Result.isError)

[<Fact>]
let ``accepting moves the promise, declining leaves it standing`` () =
    let pending, _ = mustOk (Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 15)))

    match Reschedule.apply now pending (AcceptProposal ActorRole.Customer) with
    | Ok (r, PromiseMoved at) ->
        Assert.Equal(promised.AddMinutes 15.0, at)
        Assert.Equal(promised.AddMinutes 15.0, r.PromisedStart)
        Assert.True r.Pending.IsNone
    | other -> failwithf "expected the promise to move, got %A" other

    match Reschedule.apply now pending (DeclineProposal ActorRole.Customer) with
    | Ok (r, PromiseStands at) ->
        Assert.Equal(promised, at)
        Assert.Equal(promised, r.PromisedStart)
        Assert.True r.Pending.IsNone
    | other -> failwithf "expected the promise to stand, got %A" other

[<Fact>]
let ``an expired proposal can no longer be answered, only lapsed`` () =
    let pending, _ = mustOk (Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 15)))
    let after = now + Reschedule.proposalWindow + TimeSpan.FromSeconds 1.0
    Assert.True(Reschedule.apply after pending (AcceptProposal ActorRole.Customer) |> Result.isError)
    // ...and it cannot lapse early, so a client that fires it on the wrong tick
    // gets an error rather than silently cancelling a live negotiation.
    Assert.True(Reschedule.apply now pending ExpireProposal |> Result.isError)
    match Reschedule.apply after pending ExpireProposal with
    | Ok (r, ProposalLapsed at) ->
        Assert.Equal(promised, at)
        Assert.True r.Pending.IsNone
    | other -> failwithf "expected a lapse, got %A" other

[<Fact>]
let ``a pending proposal does not pause the no-show countdown`` () =
    // This is the escalation beat, not an oversight: a provider who asks for
    // more time and is declined watches the original deadline keep running.
    let pending, _ = mustOk (Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 15)))
    Assert.Equal(Reschedule.noShowDeadline booked, Reschedule.noShowDeadline pending)
    let justAfter = Reschedule.noShowDeadline booked
    Assert.True(Reschedule.canReportNoShow justAfter pending)
    Assert.False(Reschedule.canReportNoShow (justAfter.AddSeconds -1.0) pending)

[<Fact>]
let ``accepting a later time pushes the no-show deadline out`` () =
    let pending, _ = mustOk (Reschedule.apply now booked (Propose (proposalBy ActorRole.Provider 15)))
    let agreed, _ = mustOk (Reschedule.apply now pending (AcceptProposal ActorRole.Customer))
    let wasDue = Reschedule.noShowDeadline booked
    Assert.False(Reschedule.canReportNoShow wasDue agreed)
    Assert.True(Reschedule.canReportNoShow (wasDue.AddMinutes 15.0) agreed)

// ------------------------------------------------------- State machine ----

[<Fact>]
let ``no-show is reachable only from Scheduled`` () =
    // A provider who is EnRoute is visibly moving on the map; calling that a
    // no-show contradicts the screen.
    Assert.Equal(Ok ProviderNoShow, StateMachine.transition Scheduled MarkNoShow)
    for s in [ EnRoute; Arrived; InProgress; Completed; Closed; Cancelled ] do
        Assert.True(StateMachine.transition s MarkNoShow |> Result.isError)

// ----------------------------------------------------------- BookingSlot ----

[<Fact>]
let ``every offered slot resolves, and nothing else does`` () =
    // The list and the resolver are the same contract seen twice. An option
    // added to one without the other books a job at a time nobody chose.
    for label in BookingSlot.options do
        Assert.True((BookingSlot.tryResolve label now).IsSome, label)
    Assert.True((BookingSlot.tryResolve "whenever" now).IsNone)
    Assert.True((BookingSlot.tryResolve "" now).IsNone)

[<Fact>]
let ``"Now" leaves time for someone to actually travel`` () =
    // An instant arrival reads as fake faster than a slow one reads as broken.
    let at = (BookingSlot.tryResolve "Now" now).Value
    Assert.Equal(now + BookingSlot.asapLead, at)
    Assert.True(at > now)

[<Property>]
let ``no slot ever resolves into the past`` (hoursFromEpoch: int) =
    let demoNow = DemoClock.epoch.AddHours(float (abs hoursFromEpoch % 2000))
    BookingSlot.options
    |> List.forall (fun label ->
        match BookingSlot.tryResolve label demoNow with
        | Some at -> at > demoNow
        | None -> false)

[<Fact>]
let ``Saturday means the next one, never today`` () =
    // Booking a slot in the past is worse than booking it a week out.
    let saturday = DateTimeOffset(2026, 1, 3, 14, 0, 0, TimeSpan.Zero)
    Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek)
    let at = (BookingSlot.tryResolve "Saturday morning" saturday).Value
    Assert.Equal(DayOfWeek.Saturday, at.DayOfWeek)
    Assert.True(at > saturday)
    // From the epoch (a Thursday) it is two days out, not nine.
    let fromEpoch = (BookingSlot.tryResolve "Saturday morning" DemoClock.epoch).Value
    Assert.Equal(DateTimeOffset(2026, 1, 3, 9, 0, 0, TimeSpan.Zero), fromEpoch)

[<Fact>]
let ``a resolved slot describes itself in human terms`` () =
    let today = DemoClock.epoch.AddHours 15.0
    Assert.StartsWith("Today, ", BookingSlot.describe today DemoClock.epoch)
    Assert.StartsWith("Tomorrow, ", BookingSlot.describe (DemoClock.epoch.AddDays 1.0) DemoClock.epoch)
    Assert.StartsWith("Saturday, ", BookingSlot.describe (DemoClock.epoch.AddDays 2.0) DemoClock.epoch)

// ---------------------------------------------------------------- Travel ----

[<Fact>]
let ``ETA is never zero for a provider still moving`` () =
    // "ETA 0 min" beside a car visibly crossing the map reads as a broken
    // readout, not as an imminent arrival.
    Assert.Equal(Travel.minMinutes, Travel.minutesFor 0.0)
    Assert.Equal(Travel.minMinutes, Travel.minutesFor -5.0)
    Assert.Equal(Travel.minMinutes, Travel.minutesFor nan)
    // The case that matters, and the one an earlier version of this test
    // missed: a provider five metres away is *almost* there, which is exactly
    // when the raw formula produces a fraction of a minute and the screen
    // rounds it to zero.
    Assert.Equal(Travel.minMinutes, Travel.minutesFor 0.005)
    Assert.Equal(Travel.minMinutes, Travel.minutesFor 0.4)

[<Fact>]
let ``a provider at the door stops counting and says so`` () =
    // The floor above has a second face: once someone has *stopped*, "1 min"
    // is no longer an estimate, it is a number that never changes. Held on
    // screen it reads as a frozen app rather than as an imminent doorbell.
    Assert.True(Travel.isImminent (Travel.minutesFor 0.0))
    Assert.True(Travel.isImminent (Travel.minutesFor 0.4))
    Assert.False(Travel.isImminent (Travel.minutesFor 8.0))
    Assert.Contains("arriving now", Travel.describe 0.0)
    Assert.Contains("ETA", Travel.describe 8.0)

    // …and the countdown must follow, or the two numbers on the tracking
    // screen contradict each other again.
    let sched = { PromisedStart = DemoClock.epoch.AddMinutes 30.0; Pending = None }
    let atDoor = Countdown.forCustomer EnRoute sched (Some (Travel.minutesFor 0.0)) DemoClock.epoch
    Assert.Equal(Some "Arriving now", atDoor |> Option.map Countdown.oneLine)
    let stillDriving = Countdown.forCustomer EnRoute sched (Some (Travel.minutesFor 12.0)) DemoClock.epoch
    Assert.Equal(Some "Arriving in 22:30", stillDriving |> Option.map Countdown.oneLine)

[<Property>]
let ``ETA grows with distance and stays finite`` (a: int) (b: int) =
    let near = float (abs a % 40)
    let far = near + 1.0 + float (abs b % 40)
    let etaNear = Travel.minutesFor near
    let etaFar = Travel.minutesFor far
    etaFar >= etaNear && not (Double.IsNaN etaFar) && not (Double.IsInfinity etaFar)

[<Fact>]
let ``depart-by is the promise less the travel time`` () =
    let promised = DemoClock.epoch.AddHours 2.0
    let km = 16.0
    Assert.Equal(promised - Travel.durationFor km, Travel.departBy promised km)
    // Far enough away and you should already have left.
    Assert.True(Travel.departBy promised 400.0 < DemoClock.epoch.AddHours 1.0)

[<Fact>]
let ``the two haversine implementations are now one`` () =
    // They were separate copies — Shared for the backend, ClientShared for the
    // apps — until ETA became a contract both sides had to agree on.
    let a = 43.6650, -79.4103   // The Annex
    let b = 43.7757, -79.2578   // Scarborough Centre
    let d = Geo.distanceKm a b
    Assert.InRange(d, 17.0, 20.0)
    Assert.Equal(0.0, Geo.distanceKm a a)

// ------------------------------------------------------------- JobStatus ----

let private allStates =
    [ Scheduled; EnRoute; Arrived; InProgress; Completed; Closed; Cancelled; ProviderNoShow ]

[<Fact>]
let ``every state has real copy on both sides, and never its own enum name`` () =
    // The tell this replaces: both apps matched on strings ending `| s -> s`,
    // so a state without copy rendered as "ProviderNoShow" to a user. Matching
    // on the union makes a missing case a compile error; this catches the
    // subtler failure of "copy" that is just the case name.
    for st in allStates do
        let raw = JobStateCodec.ofState st
        for copy in [ JobStatus.forCustomer st; JobStatus.forProvider st ] do
            Assert.False(String.IsNullOrWhiteSpace copy, raw)
            Assert.NotEqual<string>(raw, copy)

[<Fact>]
let ``state strings round trip, and an unknown one degrades rather than crashes`` () =
    for st in allStates do
        Assert.Equal(Some st, JobStateCodec.tryParse (JobStateCodec.ofState st))
    Assert.True((JobStateCodec.tryParse "Teleported").IsNone)
    Assert.Throws<Exception>(fun () -> JobStateCodec.parse "Teleported" |> ignore) |> ignore

[<Fact>]
let ``terminal states offer no next action and are never in flight`` () =
    // ProviderNoShow in the in-flight set would pin a dead job as the
    // provider's one active job forever.
    for st in [ Completed; Closed; Cancelled; ProviderNoShow ] do
        Assert.True((JobStatus.nextProviderAction st).IsNone, JobStateCodec.ofState st)
        Assert.False(JobStatus.isInFlight st, JobStateCodec.ofState st)

[<Fact>]
let ``the next action's event is one the state machine will actually accept`` () =
    // A button whose label promises a transition the machine rejects is worse
    // than no button.
    for st in allStates do
        match JobStatus.nextProviderAction st with
        | Some (_, ev) -> Assert.True(StateMachine.transition st ev |> Result.isOk, JobStateCodec.ofState st)
        | None -> ()

[<Fact>]
let ``only a pending arrival counts down`` () =
    Assert.True(JobStatus.awaitsArrival Scheduled)
    Assert.True(JobStatus.awaitsArrival EnRoute)
    for st in [ Arrived; InProgress; Completed; Closed; Cancelled; ProviderNoShow ] do
        Assert.False(JobStatus.awaitsArrival st, JobStateCodec.ofState st)

// ---------------------------------------------------------------- Notify ----

let private notice id kind text =
    Notify.create id kind (Some 7) DemoClock.epoch text

[<Fact>]
let ``the queue keeps several notices instead of replacing one`` () =
    let q =
        []
        |> Notify.push (notice 1 NoticeKind.Info "first")
        |> Notify.push (notice 2 NoticeKind.Success "second")
    Assert.Equal(2, List.length q)
    // Newest first — the order they are read in.
    Assert.Equal("second", (List.head q).Text)

[<Fact>]
let ``the stack is bounded, oldest dropped first`` () =
    let q =
        [ for i in 1 .. Notify.maxVisible + 3 -> notice i NoticeKind.Info (string i) ]
        |> List.fold (fun acc n -> Notify.push n acc) []
    Assert.Equal(Notify.maxVisible, List.length q)
    Assert.DoesNotContain("1", q |> List.map (fun n -> n.Text))

[<Fact>]
let ``an unanswered question is never dropped to make room`` () =
    // Discarding an Ask is a correctness bug, not a display one: the other
    // party is waiting on an answer that can no longer be given.
    let q =
        [ notice 1 NoticeKind.Ask "Provider wants to push back 15 min" ]
        |> List.append []
        |> fun start ->
            [ for i in 2 .. 8 -> notice i NoticeKind.Info (string i) ]
            |> List.fold (fun acc n -> Notify.push n acc) start
    Assert.Contains(q, fun n -> n.Kind = NoticeKind.Ask)

[<Fact>]
let ``pruning is a pure function of demo time, and asks never expire`` () =
    let q =
        []
        |> Notify.push (notice 1 NoticeKind.Info "fades")
        |> Notify.push (notice 2 NoticeKind.Ask "waits for an answer")
    Assert.Equal(2, List.length (Notify.prune DemoClock.epoch q))
    let later = DemoClock.epoch + Notify.lifetime + TimeSpan.FromSeconds 1.0
    let survivors = Notify.prune later q
    Assert.Equal(1, List.length survivors)
    Assert.Equal(NoticeKind.Ask, (List.head survivors).Kind)

[<Fact>]
let ``clearing a job takes its notices with it`` () =
    // A job that closes should not leave "your provider is running late" up.
    let q =
        []
        |> Notify.push (Notify.create 1 NoticeKind.Info (Some 7) DemoClock.epoch "about job 7")
        |> Notify.push (Notify.create 2 NoticeKind.Info (Some 9) DemoClock.epoch "about job 9")
        |> Notify.push (Notify.create 3 NoticeKind.Info None DemoClock.epoch "about the account")
    let remaining = Notify.clearJob 7 q
    Assert.Equal(2, List.length remaining)
    Assert.DoesNotContain("about job 7", remaining |> List.map (fun n -> n.Text))

[<Fact>]
let ``classification separates an alarm from an acknowledgement`` () =
    Assert.Equal(NoticeKind.Warning, Notify.classify "Provider is running late")
    Assert.Equal(NoticeKind.Warning, Notify.classify "Reported as a no-show")
    Assert.Equal(NoticeKind.Success, Notify.classify "Provider Accepted")
    Assert.Equal(NoticeKind.Success, Notify.classify "Payment Complete")
    // Unrecognised text stays quiet rather than falsely alarming.
    Assert.Equal(NoticeKind.Info, Notify.classify "Something new happened")

// ------------------------------------------------------------- Countdown ----

let private booking (minutesAhead: float) =
    Reschedule.ofBooking (DemoClock.epoch.AddMinutes minutesAhead)

let private mustHave (c: Countdown option) =
    match c with Some v -> v | None -> failwith "expected a countdown, got none"

[<Fact>]
let ``the two sides count down to different things at the same instant`` () =
    // The whole point of the module. "Arriving in 30:00" and "Leave in 15:00"
    // are the same job at the same moment, and each is the only number that
    // changes its reader's behaviour.
    let r = booking 30.0
    let cust = mustHave (Countdown.forCustomer Scheduled r None DemoClock.epoch)
    // 8 km at 32 km/h is a 15-minute drive, so departure is 15 minutes out.
    let prov = mustHave (Countdown.forProvider Scheduled r (Some 8.0) DemoClock.epoch)
    Assert.Equal("Arriving in", cust.Label)
    Assert.Equal("Leave in", prov.Label)
    Assert.NotEqual<string>(cust.Value, prov.Value)

[<Fact>]
let ``once the provider should have left, the number becomes the deadline`` () =
    let r = booking 30.0
    // Ten minutes in, a 15-minute drive means departure has passed.
    let late = DemoClock.epoch.AddMinutes 20.0
    let prov = mustHave (Countdown.forProvider Scheduled r (Some 8.0) late)
    Assert.StartsWith("Leave now", prov.Label)
    Assert.Equal(Urgency.Urgent, prov.Urgency)

[<Fact>]
let ``past the promise the provider is told when it becomes reportable`` () =
    let r = booking 30.0
    let after = DemoClock.epoch.AddMinutes 35.0
    let prov = mustHave (Countdown.forProvider Scheduled r (Some 8.0) after)
    Assert.Contains("no-show", prov.Label)
    Assert.Equal(Urgency.Overdue, prov.Urgency)
    // Counting to the grace deadline, not to the promise already missed.
    Assert.Equal(Format.countdown (Reschedule.noShowDeadline r - after), prov.Value)

[<Fact>]
let ``once past the grace deadline the provider is not told to keep waiting`` () =
    // Caught in the live two-app run: a single sign-blind `.Duration()` value
    // read "reportable as a no-show in 11:48" when the deadline was already
    // 11:48 *past* — telling the provider to wait while the customer could
    // report them right then. The label must flip, like the customer's does.
    let r = booking 30.0                       // promised = epoch + 30
    let deadline = Reschedule.noShowDeadline r // = promised + 15 = epoch + 45
    let wellPast = deadline.AddMinutes 12.0     // 12 minutes past reportable
    let prov = mustHave (Countdown.forProvider Scheduled r (Some 8.0) wellPast)
    // No longer the "in X" phrasing that implies time remaining…
    Assert.DoesNotContain("reportable as a no-show in", prov.Label)
    // …and the value is how long it has been overdue, counted forward.
    Assert.Equal(Format.countdown (wellPast - deadline), prov.Value)
    Assert.Equal(Urgency.Overdue, prov.Urgency)

[<Fact>]
let ``a live ETA that cannot make the promise is shown as overdue`` () =
    // The reconciliation the plan asked for: a provider already behind does
    // not become on time by driving fast, and the screen must not show a
    // rosier number than the one both parties agreed to.
    let r = booking 10.0
    let onTime = mustHave (Countdown.forCustomer EnRoute r (Some 5.0) DemoClock.epoch)
    Assert.NotEqual(Urgency.Overdue, onTime.Urgency)
    let cannotMakeIt = mustHave (Countdown.forCustomer EnRoute r (Some 25.0) DemoClock.epoch)
    Assert.Equal(Urgency.Overdue, cannotMakeIt.Urgency)

[<Fact>]
let ``a pending proposal outranks the job's own countdown, for both parties`` () =
    // It has its own deadline, and lapsing unanswered is the one outcome
    // nobody chose.
    let r = booking 60.0
    let pending, _ =
        mustOk (Reschedule.apply DemoClock.epoch r
                    (Propose { ProposedStart = r.PromisedStart.AddMinutes 15.0
                               By = ActorRole.Provider
                               Reason = "Traffic"
                               ExpiresAt = DemoClock.epoch + Reschedule.proposalWindow }))
    let cust = mustHave (Countdown.forCustomer Scheduled pending None DemoClock.epoch)
    let prov = mustHave (Countdown.forProvider Scheduled pending (Some 8.0) DemoClock.epoch)
    Assert.Contains("proposed", cust.Label)
    Assert.Contains("Awaiting", prov.Label)
    // Urgent regardless of the clock: an unanswered question is not calm.
    Assert.Equal(Urgency.Urgent, cust.Urgency)
    Assert.Equal(Urgency.Urgent, prov.Urgency)

[<Fact>]
let ``states with nothing to wait for show no countdown at all`` () =
    // A stale clock on a finished job is worse than no clock.
    let r = booking 30.0
    for st in [ Arrived; InProgress; Completed; Closed; Cancelled; ProviderNoShow ] do
        Assert.True((Countdown.forCustomer st r None DemoClock.epoch).IsNone, JobStateCodec.ofState st)
        Assert.True((Countdown.forProvider st r (Some 8.0) DemoClock.epoch).IsNone, JobStateCodec.ofState st)

[<Property>]
let ``urgency only ever escalates as demo time advances`` (aheadSeed: int) (stepSeed: int) =
    // Guards the readout against ever relaxing on its own — a countdown that
    // goes from Urgent back to Calm reads as a bug even when the number is
    // right.
    let r = booking (1.0 + float (abs aheadSeed % 120))
    let step = float (abs stepSeed % 30)
    let rank u = match u with
                 | Urgency.Calm -> 0 | Urgency.Soon -> 1
                 | Urgency.Urgent -> 2 | Urgency.Overdue -> 3
    let earlier = Countdown.forCustomer Scheduled r None DemoClock.epoch
    let later = Countdown.forCustomer Scheduled r None (DemoClock.epoch.AddMinutes step)
    match earlier, later with
    | Some a, Some b -> rank b.Urgency >= rank a.Urgency
    | _ -> true

[<Fact>]
let ``a passed deadline is worded as passed, not as an arrival with a suffix`` () =
    // Caught on the running app, not by a test: the customer's Home screen read
    // "Arriving in 3:06 late", because one sign-blind label was composed with a
    // value that appended "late". The label carries direction now.
    let r = booking 8.0
    let overdue = mustHave (Countdown.forCustomer Scheduled r None (DemoClock.epoch.AddMinutes 11.0))
    Assert.Equal(Urgency.Overdue, overdue.Urgency)
    Assert.DoesNotContain("Arriving in", overdue.Label)
    // The value is a bare duration; no phrasing leaks into it from either side.
    Assert.DoesNotContain("late", overdue.Value)
    Assert.DoesNotContain("-", overdue.Value)
    Assert.Equal("Late by 3:00", Countdown.oneLine overdue)

[<Property>]
let ``no countdown ever reads as both directions at once`` (aheadSeed: int) (nowSeed: int) =
    // The general form of the same defect: whatever the sign, exactly one
    // direction may appear in the rendered line.
    let r = booking (1.0 + float (abs aheadSeed % 60))
    let now = DemoClock.epoch.AddMinutes(float (abs nowSeed % 120))
    [ Countdown.forCustomer Scheduled r None now
      Countdown.forProvider Scheduled r (Some 8.0) now ]
    |> List.choose id
    |> List.forall (fun c ->
        let line = (Countdown.oneLine c).ToLowerInvariant()
        not (line.Contains "arriving in" && line.Contains "late"))

[<Fact>]
let ``times render in the demo timeline, not the operator's timezone`` () =
    // Caught on the device: the countdown read 00:23 while the proposed time
    // beside it read 7:23 PM, because clockTime shifted a demo instant into the
    // machine's local zone. Demo instants are on a fictional timeline; there is
    // no "local" to convert them to.
    let iso = DemoClock.epoch.AddMinutes(23.0).ToString "o"
    Assert.Equal("12:23 AM", Format.clockTime iso)
    Assert.Equal("1 Jan", Format.shortDate iso)

[<Fact>]
let ``cancellation says whose decision it was, from each side`` () =
    // "Cancelled" alone cannot tell a customer changing their mind from a
    // provider dropping the job, and a marketplace cannot be indifferent to
    // that difference.
    let c, p = ActorRole.Customer, ActorRole.Provider
    Assert.Equal("You cancelled this booking", JobStatus.cancelledBy c (Some c))
    Assert.Equal("Your provider cancelled this booking", JobStatus.cancelledBy c (Some p))
    Assert.Equal("You cancelled this job", JobStatus.cancelledBy p (Some p))
    Assert.Equal("The customer cancelled this job", JobStatus.cancelledBy p (Some c))
    // Older rows carry no actor; the copy degrades rather than guessing.
    Assert.Equal("This job was cancelled", JobStatus.cancelledBy c None)
