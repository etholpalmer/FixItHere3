namespace FixItHere.Shared

open System

/// One party's request to move the agreed arrival time.
type Proposal =
    { ProposedStart: DateTimeOffset
      /// Who asked. Needed to stop a party accepting its own proposal, which
      /// would turn a bilateral negotiation into a unilateral edit.
      By: ActorRole
      Reason: string
      /// Demo instant. An unanswered proposal must lapse on its own, or a
      /// customer who puts their phone down leaves the provider waiting on a
      /// dialog that never resolves.
      ExpiresAt: DateTimeOffset }

/// The reschedule sub-status of a job.
///
/// Deliberately *not* a `JobState` case. `JobStateCodec.toState` `failwithf`s
/// on anything it does not recognise, so a state carrying a payload
/// (`RescheduleProposed of DateTimeOffset`) would serialise as
/// `"RescheduleProposed 2026-01-01..."` and hard-crash on read-back. The job
/// stays `Scheduled` throughout a negotiation — which is also how the project
/// glossary defines Reschedule: moving a time *without* triggering the
/// cancellation path.
type Reschedule =
    { /// The arrival time that currently stands. Starts equal to the booked
      /// time and only moves when a proposal is accepted.
      PromisedStart: DateTimeOffset
      Pending: Proposal option }

type RescheduleEvent =
    | Propose of Proposal
    | AcceptProposal of by: ActorRole
    | DeclineProposal of by: ActorRole
    /// Fired by whichever side notices the clock passed `ExpiresAt`. Both may
    /// notice; applying it twice is an error, not a silent no-op, so a double
    /// fire is visible in tests rather than masked.
    | ExpireProposal

/// What happened, for the notification layer to render. Separate from the new
/// state because "declined" and "lapsed" leave identical data behind but need
/// different copy.
type RescheduleOutcome =
    | ProposalRaised of Proposal
    | PromiseMoved of DateTimeOffset
    | PromiseStands of DateTimeOffset
    | ProposalLapsed of DateTimeOffset

module Reschedule =

    /// Grace after the promised arrival before a no-show may be reported.
    /// Fifteen minutes is long enough to read as a real policy and short enough
    /// that at 60x the audience watches it expire in fifteen seconds.
    let graceMinutes = 15.0

    /// How long a proposal stays open before it lapses.
    let proposalWindow = TimeSpan.FromMinutes 5.0

    let ofBooking (start: DateTimeOffset) = { PromisedStart = start; Pending = None }

    /// When the no-show path unlocks.
    ///
    /// Derived from the promise that currently stands, and therefore *not*
    /// paused by a pending proposal. That is the escalation beat working as
    /// designed: a provider who asks for more time and is declined watches the
    /// original deadline keep running, because declining left the original
    /// promise in force.
    let noShowDeadline (r: Reschedule) = r.PromisedStart.AddMinutes graceMinutes

    let canReportNoShow (demoNow: DateTimeOffset) (r: Reschedule) = demoNow >= noShowDeadline r

    /// When the provider must leave to keep the promise — the number that
    /// actually changes their behaviour, and so the one their countdown shows.
    let departBy (travel: TimeSpan) (r: Reschedule) = r.PromisedStart - travel

    /// The transition function. Pure, total, and the only way `Reschedule`
    /// changes: every guard below is a rule the UI would otherwise have to
    /// remember to enforce in two apps independently.
    let apply (demoNow: DateTimeOffset) (r: Reschedule) (ev: RescheduleEvent)
        : Result<Reschedule * RescheduleOutcome, string> =

        // Shared by accept/decline: both need a live proposal raised by the
        // *other* party.
        let liveProposalFor (by: ActorRole) =
            match r.Pending with
            | None -> Error "There is no proposal to answer."
            | Some p when p.By = by -> Error "The party that proposed a new time cannot answer its own proposal."
            | Some p when demoNow >= p.ExpiresAt -> Error "That proposal has expired."
            | Some p -> Ok p

        match ev with
        | Propose p ->
            if r.Pending.IsSome then Error "A proposal is already awaiting an answer."
            elif p.ProposedStart <= demoNow then Error "Cannot promise a time that has already passed."
            elif p.ProposedStart = r.PromisedStart then Error "That is the time already agreed."
            elif p.ExpiresAt <= demoNow then Error "That proposal would already have expired."
            else Ok ({ r with Pending = Some p }, ProposalRaised p)

        | AcceptProposal by ->
            liveProposalFor by
            |> Result.map (fun p ->
                { PromisedStart = p.ProposedStart; Pending = None }, PromiseMoved p.ProposedStart)

        | DeclineProposal by ->
            liveProposalFor by
            |> Result.map (fun _ ->
                // The original promise stands, so the no-show countdown that
                // prompted the request resumes rather than restarting.
                { r with Pending = None }, PromiseStands r.PromisedStart)

        | ExpireProposal ->
            match r.Pending with
            | None -> Error "There is no proposal to expire."
            | Some p when demoNow < p.ExpiresAt -> Error "That proposal has not expired yet."
            | Some _ -> Ok ({ r with Pending = None }, ProposalLapsed r.PromisedStart)
