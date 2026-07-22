namespace FixItHere.Shared

type JobState =
    | Scheduled
    | EnRoute
    | Arrived
    | InProgress
    | Completed
    | Closed
    | Cancelled
    /// The provider never turned up. Nullary and terminal, so it round-trips
    /// through `JobStateCodec` safely — unlike a reschedule state, which would
    /// need to carry a time. Distinct from `Cancelled` because "nobody came"
    /// and "someone called it off" are different stories, and the demo's
    /// escalation beat is only legible if the outcome says which happened.
    | ProviderNoShow

type JobEvent =
    | Accepted      // provider takes the job (stays Scheduled in demo; accept marks assignment)
    | DepartEnRoute
    | Arrive
    | StartWork
    | CompleteWork
    | RateAndClose
    | Cancel
    /// Only reachable once the grace window past the promised arrival has
    /// elapsed — `Reschedule.canReportNoShow` is the gate, enforced by the
    /// caller because the state machine has no clock.
    | MarkNoShow

type JobKind = Service

/// What a trade charges. Every booked job used to cost exactly $85.00 whatever
/// the trade, and seeded jobs carried an unrelated random number — a plumbing
/// call and a house clean priced identically is the kind of detail a sceptical
/// viewer checks. Decomposed rather than a single figure because the receipt
/// has to show its working: call-out + labour is what an invoice looks like.
type ServiceRate =
    { /// Fixed charge for turning up. Trades that quote by the job (painting,
      /// moving, cleaning) do not levy one; emergency trades do.
      CallOutFee: decimal
      HourlyRate: decimal
      /// How long this trade typically takes on site. Also the basis for the
      /// quoted price at booking time, before any actual duration is known.
      TypicalMinutes: int }

module ServiceRate =
    /// GTA market rates. Deliberately not round numbers — $125/hr reads as a
    /// price someone set, $100/hr reads as a placeholder.
    let forService (name: string) =
        match name with
        | "Plumbing"   -> { CallOutFee = 90m;  HourlyRate = 125m; TypicalMinutes = 90 }
        | "Electrical" -> { CallOutFee = 85m;  HourlyRate = 115m; TypicalMinutes = 120 }
        | "Mechanic"   -> { CallOutFee = 110m; HourlyRate = 135m; TypicalMinutes = 90 }
        | "HVAC"       -> { CallOutFee = 120m; HourlyRate = 140m; TypicalMinutes = 120 }
        | "Painting"   -> { CallOutFee = 0m;   HourlyRate = 55m;  TypicalMinutes = 240 }
        | "Moving"     -> { CallOutFee = 0m;   HourlyRate = 150m; TypicalMinutes = 180 }
        | "Cleaning"   -> { CallOutFee = 0m;   HourlyRate = 50m;  TypicalMinutes = 180 }
        | _            -> { CallOutFee = 75m;  HourlyRate = 100m; TypicalMinutes = 120 }

    let labour (rate: ServiceRate) (minutes: int) =
        System.Math.Round(rate.HourlyRate * decimal minutes / 60m, 2)

    /// Call-out plus labour for a given duration.
    let total (rate: ServiceRate) (minutes: int) =
        rate.CallOutFee + labour rate minutes

    /// What a job is quoted at booking, before the work is done.
    let quote (name: string) =
        let r = forService name
        total r r.TypicalMinutes

/// Marketplace economics. These are the numbers an investor is actually buying,
/// so they belong in one place rather than being implied by a single total.
module Money =
    /// Ontario HST.
    let taxRate = 0.13m
    /// The platform's cut of the subtotal. 15% is mid-market for a services
    /// marketplace — high enough to be a business, low enough to be defensible.
    let platformFeeRate = 0.15m

    let private r2 (d: decimal) = System.Math.Round(d, 2)

    /// Splits a job's charge into the lines a receipt has to show. `total` is
    /// the price already agreed on the job, so the breakdown always reconciles
    /// to what the customer was quoted rather than being recomputed and drifting.
    let breakdown (callOut: decimal) (labourMinutes: int) (labour: decimal) =
        let subtotal = r2 (callOut + labour)
        let tax = r2 (subtotal * taxRate)
        let platformFee = r2 (subtotal * platformFeeRate)
        {| CallOutFee = callOut
           LabourMinutes = labourMinutes
           LabourAmount = r2 labour
           Subtotal = subtotal
           Tax = tax
           Total = r2 (subtotal + tax)
           PlatformFee = platformFee
           ProviderPayout = r2 (subtotal - platformFee) |}

module ServiceNames =
    let all = ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"]
