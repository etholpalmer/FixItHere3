module FixItHere.Backend.Clock

open System

open FixItHere.Shared
open FixItHere.Shared.Dtos

/// The demo clock lives in **process memory**, not the database.
///
/// The plan required stating where the anchor lives and why, because both
/// obvious answers are wrong in a different way and the choice is invisible
/// until it breaks mid-demo.
///
/// It is not persisted because `Program.fs` drops, recreates and reseeds the
/// SQLite file on every boot, and `POST /dev/reset` does the same thing live.
/// A persisted anchor would therefore *outlive the data it refers to*: after a
/// reset the jobs are all freshly anchored at the epoch while the clock is
/// still hours ahead of them, so every job in the list is instantly overdue.
/// The clock and the seed have to be reset by the same act, and the seed is
/// authoritative about when the world starts.
///
/// The cost of process memory is that a backend restart resets the clock. That
/// is acceptable and arguably correct: a restart also reseeds, so the whole
/// world resets together. It is exactly the coupling that persistence would
/// break.
type DemoClockService() =
    // Writes are read-modify-write, so they need the lock. Reads take the
    // reference without one: `DemoClock` is an immutable record, so a reader
    // either sees the whole old value or the whole new one — never a torn mix.
    let gate = obj ()
    let mutable current = DemoClock.start DateTimeOffset.UtcNow

    member _.Current = current

    /// Demo time, right now. Every countdown in the product resolves to this.
    member _.Now() = DemoClock.nowAt current DateTimeOffset.UtcNow

    /// Apply a mutation. The real instant is captured *inside* the lock so two
    /// concurrent operator clicks cannot re-anchor against different "now"s.
    member _.Apply(f: DateTimeOffset -> DemoClock -> DemoClock) =
        lock gate (fun () ->
            current <- f DateTimeOffset.UtcNow current
            current)

    /// Called alongside a reseed, never on its own.
    member _.Reset() =
        lock gate (fun () ->
            current <- DemoClock.start DateTimeOffset.UtcNow
            current)

let toDto (c: DemoClock) (realNow: DateTimeOffset) : DemoClockDto =
    { DemoNow = (DemoClock.nowAt c realNow).ToString "o"
      AnchorDemo = c.AnchorDemo.ToString "o"
      AnchorReal = c.AnchorReal.ToString "o"
      Rate = c.Rate
      Running = c.Running }

/// Parse an operator command into a clock mutation.
///
/// Pure and total: an unrecognised action is an error the endpoint reports,
/// not a silent no-op that leaves the console's buttons looking dead.
let interpret (req: SetClockRequest) : Result<DateTimeOffset -> DemoClock -> DemoClock, string> =
    match req.Action with
    | "pause"  -> Ok DemoClock.pause
    | "resume" -> Ok DemoClock.resume
    | "rate"   -> Ok (DemoClock.withRate req.Rate)
    | "jump" ->
        match DateTimeOffset.TryParse(req.Target, Globalization.CultureInfo.InvariantCulture,
                                      Globalization.DateTimeStyles.RoundtripKind) with
        | true, target -> Ok (DemoClock.jumpTo target)
        | _ -> Error (sprintf "Cannot parse '%s' as a demo instant." req.Target)
    | other -> Error (sprintf "Unknown clock action '%s'. Expected pause, resume, rate or jump." other)
