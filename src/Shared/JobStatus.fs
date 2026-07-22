namespace FixItHere.Shared

/// Job state as a string, and back.
///
/// Moved out of the backend's `Db.fs` because the apps need the same decode.
/// While it lived server-side the two apps matched on raw strings with a
/// `| s -> s` fall-through, so any state the copy did not know about rendered
/// its own enum name to the user — "ProviderNoShow" in a status line.
module JobStateCodec =
    let ofState (s: JobState) = sprintf "%A" s

    /// Total. Clients use this: a state a client does not recognise should
    /// degrade to honest copy, never take the app down.
    let tryParse (s: string) =
        match s with
        | "Scheduled" -> Some Scheduled
        | "EnRoute" -> Some EnRoute
        | "Arrived" -> Some Arrived
        | "InProgress" -> Some InProgress
        | "Completed" -> Some Completed
        | "Closed" -> Some Closed
        | "Cancelled" -> Some Cancelled
        | "ProviderNoShow" -> Some ProviderNoShow
        | _ -> None

    /// Loud. The server uses this: a state it wrote and cannot read back is a
    /// bug to surface immediately, not to paper over.
    let parse (s: string) =
        match tryParse s with
        | Some st -> st
        | None -> failwithf "Unknown job state '%s'" s

/// What each side is told a job's state means.
///
/// Both apps used to keep their own `match` over state strings, and the plan's
/// audit found they had already drifted. Matching on `JobState` rather than on
/// `string` is the load-bearing part: adding a case to the DU makes every
/// function below a compile error until someone writes the words a user will
/// read. That is the only mechanism that actually keeps copy honest.
module JobStatus =

    let forCustomer =
        function
        | Scheduled -> "Booked — waiting for your provider to head out"
        | EnRoute -> "Your provider is on the way"
        | Arrived -> "Your provider has arrived"
        | InProgress -> "Work in progress"
        | Completed -> "Work complete — settling up"
        | Closed -> "Finished"
        // Deliberately neutral about who called it off. `Cancelled` carries no
        // actor yet — the plan's asymmetry table has that as task 12 — and
        // guessing wrong is worse than not saying.
        | Cancelled -> "This booking was cancelled"
        | ProviderNoShow -> "Your provider never arrived"

    let forProvider =
        function
        | Scheduled -> "Ready to head out"
        | EnRoute -> "You're on the way"
        | Arrived -> "You have arrived"
        | InProgress -> "Work in progress"
        | Completed -> "Work complete — awaiting payment"
        | Closed -> "Finished"
        | Cancelled -> "This job was cancelled"
        | ProviderNoShow -> "Reported as a no-show"

    /// The one clear next action, as an event the app maps to its own Msg.
    /// Returning a `JobEvent` rather than a label alone keeps the button and
    /// the transition it triggers from disagreeing.
    let nextProviderAction =
        function
        | Scheduled -> Some ("Depart", DepartEnRoute)
        | EnRoute -> Some ("Arrived", Arrive)
        | Arrived -> Some ("Start Work", StartWork)
        | InProgress -> Some ("Complete", CompleteWork)
        | Completed | Closed | Cancelled | ProviderNoShow -> None

    /// Cancellation, worded by who did it.
    ///
    /// `Cancelled` alone was indistinguishable from a leaked enum name and said
    /// nothing about whose decision it was. The actor is a job field now, so
    /// the copy can finally be specific.
    let cancelledBy (forRole: ActorRole) (by: ActorRole option) =
        match forRole, by with
        | ActorRole.Customer, Some ActorRole.Customer -> "You cancelled this booking"
        | ActorRole.Customer, Some ActorRole.Provider -> "Your provider cancelled this booking"
        | ActorRole.Provider, Some ActorRole.Provider -> "You cancelled this job"
        | ActorRole.Provider, Some ActorRole.Customer -> "The customer cancelled this job"
        | _, None -> "This job was cancelled"

    /// A job someone is actively working. Terminal states are not in flight —
    /// including ProviderNoShow, which would otherwise keep a dead job pinned
    /// as the provider's one active job forever.
    let isInFlight =
        function
        | EnRoute | Arrived | InProgress -> true
        | Scheduled | Completed | Closed | Cancelled | ProviderNoShow -> false

    /// Whether an arrival is still ahead of us — the only states where a
    /// countdown to arrival, or a lateness readout, means anything.
    let awaitsArrival =
        function
        | Scheduled | EnRoute -> true
        | Arrived | InProgress | Completed | Closed | Cancelled | ProviderNoShow -> false
