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

module ServiceNames =
    let all = ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"]
