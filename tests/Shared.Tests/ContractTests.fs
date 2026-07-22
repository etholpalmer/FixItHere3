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
