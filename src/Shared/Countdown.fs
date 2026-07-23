namespace FixItHere.Shared

open System

/// How much attention a countdown deserves. Drives colour and weight, so a
/// deadline that has passed cannot look like one that is comfortably ahead.
[<RequireQualifiedAccess>]
type Urgency =
    | Calm
    | Soon
    | Urgent
    | Overdue

/// One line of time pressure, ready to render.
type Countdown =
    { /// What is being counted down to, in the reader's own terms.
      Label: string
      /// The clock itself, from `Format.countdown`.
      Value: string
      Urgency: Urgency }

/// The countdown each side sees, given the same facts.
///
/// Role- and state-contextual on purpose: "arriving in 8:04" and "leave in
/// 12:30" are the *same* job at the *same* instant, and each is the only
/// number that changes its reader's behaviour. A single shared "time until
/// arrival" would be honest and useless to the provider.
///
/// Everything here is a pure function of `demoNow`, which is what makes the
/// whole design work: no timer is scheduled anywhere, so moving a deadline
/// cannot strand a callback — the next frame simply subtracts a different
/// number.
module Countdown =

    /// Below this a countdown is the most important thing on the screen.
    let urgentWithin = TimeSpan.FromMinutes 5.0
    let soonWithin = TimeSpan.FromMinutes 15.0

    let urgencyOf (remaining: TimeSpan) =
        if remaining < TimeSpan.Zero then Urgency.Overdue
        elif remaining <= urgentWithin then Urgency.Urgent
        elif remaining <= soonWithin then Urgency.Soon
        else Urgency.Calm

    /// Two labels, because a deadline reads differently once it has passed.
    ///
    /// Composing one sign-blind label with a value that appends "late" produced
    /// "Arriving in 3:06 late" on the customer's Home screen — caught by
    /// looking at the running app, not by any test. The value is always the
    /// absolute duration; the *label* carries the direction.
    let private at (ahead: string) (behind: string) (deadline: DateTimeOffset) (demoNow: DateTimeOffset) =
        let remaining = deadline - demoNow
        { Label = (if remaining < TimeSpan.Zero then behind else ahead)
          Value = Format.countdown (remaining.Duration())
          Urgency = urgencyOf remaining }

    /// A live proposal outranks whatever else the job is doing, for both
    /// parties: it has its own deadline, and letting it expire unanswered is
    /// the one outcome nobody chose.
    let private pending (r: Reschedule) (labelFor: ActorRole -> string) (demoNow: DateTimeOffset) =
        r.Pending
        |> Option.map (fun p ->
            { at (labelFor p.By) "Proposal expired" p.ExpiresAt demoNow with
                // Always urgent regardless of the clock: an unanswered question
                // is not a calm state even with four minutes left on it.
                Urgency = Urgency.Urgent })

    /// What the customer is waiting on.
    ///
    /// `etaMinutes` is the live distance-derived estimate, available only once
    /// the provider is moving. It is *reconciled against the promise*: if the
    /// car cannot make the agreed time, the screen says so rather than quietly
    /// showing a rosier number than the one both parties agreed to.
    let forCustomer (state: JobState) (r: Reschedule) (etaMinutes: float option)
                    (demoNow: DateTimeOffset) : Countdown option =
        match pending r (fun by ->
                match by with
                | ActorRole.Provider -> "New time proposed — reply within"
                | ActorRole.Customer -> "Waiting on your provider") demoNow with
        | Some c -> Some c
        | None ->

        match state with
        | Scheduled ->
            if demoNow >= Reschedule.noShowDeadline r then
                Some { Label = "Your provider never arrived — overdue by"
                       Value = Format.countdown (demoNow - r.PromisedStart)
                       Urgency = Urgency.Overdue }
            else Some (at "Arriving in" "Late by" r.PromisedStart demoNow)
        | EnRoute ->
            match etaMinutes with
            // The provider is at the door but has not tapped Arrived yet.
            // Counting is over — the estimate has hit its floor and would sit
            // at "Arriving in 1:00" for as long as they stand there, which
            // reads as a stalled screen. Say the true thing instead. `oneLine`
            // renders this as "Arriving now", so Home reads correctly too.
            | Some mins when Travel.isImminent mins ->
                Some { Label = "Arriving"
                       Value = "now"
                       Urgency = if demoNow > r.PromisedStart then Urgency.Overdue else Urgency.Urgent }
            | Some mins ->
                let arrival = demoNow.AddMinutes mins
                // The honest number is the later of the two: a provider who is
                // already behind does not become on time by driving fast.
                if arrival > r.PromisedStart.AddMinutes 1.0 then
                    // Will arrive, but not by the time that was agreed.
                    Some { at "Arriving in" "Late by" arrival demoNow with Urgency = Urgency.Overdue }
                else Some (at "Arriving in" "Late by" arrival demoNow)
            | None -> Some (at "Arriving in" "Late by" r.PromisedStart demoNow)
        | Arrived | InProgress | Completed | Closed | Cancelled | ProviderNoShow -> None

    /// What the provider has to act on.
    ///
    /// While a job is only Scheduled the useful number is not "when do they
    /// expect me" but "when must I leave" — the one that actually changes
    /// behaviour. Once that has passed it becomes how long until the customer
    /// can report a no-show, because that is the next thing that happens.
    let forProvider (state: JobState) (r: Reschedule) (travelKm: float option)
                    (demoNow: DateTimeOffset) : Countdown option =
        match pending r (fun by ->
                match by with
                | ActorRole.Provider -> "Awaiting the customer's reply"
                | ActorRole.Customer -> "Customer proposed a new time — reply within") demoNow with
        | Some c -> Some c
        | None ->

        match state with
        | Scheduled ->
            let departBy =
                travelKm
                |> Option.map (fun km -> Travel.departBy r.PromisedStart km)
                |> Option.defaultValue r.PromisedStart
            if demoNow < departBy then Some (at "Leave in" "Should have left" departBy demoNow)
            elif demoNow < r.PromisedStart then
                Some { at "Leave now — due in" "Due" r.PromisedStart demoNow with Urgency = Urgency.Urgent }
            else
                Some { Label = "Late — reportable as a no-show in"
                       Value = Format.countdown ((Reschedule.noShowDeadline r - demoNow).Duration())
                       Urgency = Urgency.Overdue }
        | EnRoute -> Some (at "Due in" "Overdue by" r.PromisedStart demoNow)
        | Arrived | InProgress | Completed | Closed | Cancelled | ProviderNoShow -> None

    /// "Arriving in 8:04" / "Late by 3:06" — the label already carries the
    /// direction, so a row never has to re-derive it.
    let inline oneLine (c: Countdown) = sprintf "%s %s" c.Label c.Value
