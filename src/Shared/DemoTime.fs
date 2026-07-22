namespace FixItHere.Shared

open System
open System.Globalization

/// An affine map from real time to demo time.
///
/// The server holds one of these and pushes *the map*, never the time. Clients
/// extrapolate against their own wall clock, so nothing polls and — the
/// property that matters — **no client owns a countdown timer**. Every
/// countdown on every screen is `deadline - demoNow`, recomputed from this map
/// each frame.
///
/// That is what makes a moved deadline safe. When a reschedule shifts an
/// arrival by twenty minutes there is no timer to cancel and no stale callback
/// to fire; the next frame simply subtracts a different number. This codebase
/// has already shipped the stale-timer bug twice (typing indicators, auto
/// reply) and defended against it with generation tokens both times. Here the
/// bug class is structurally absent rather than guarded.
type DemoClock =
    { /// Demo instant at the anchor.
      AnchorDemo: DateTimeOffset
      /// Real instant at the anchor.
      AnchorReal: DateTimeOffset
      /// Demo seconds per real second.
      Rate: float
      Running: bool }

module DemoClock =

    /// The seed's fixed epoch, and the single source of truth for it. The seed
    /// anchors every seeded job to `Epoch + i hours`, so demo time starting
    /// here puts all thirty Scheduled jobs legitimately in the future without
    /// the seed needing to know a wall clock exists.
    [<Literal>]
    let epochIso = "2026-01-01T00:00:00Z"

    let epoch =
        DateTimeOffset.Parse(epochIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)

    /// 1x is real time. The cap exists because the demo's centrepiece is a map:
    /// past roughly two minutes of demo time per real second the provider's car
    /// teleports between location pushes instead of gliding.
    let minRate = 1.0
    let maxRate = 120.0
    let clampRate r = if Double.IsNaN r then minRate else max minRate (min maxRate r)

    let start (realNow: DateTimeOffset) =
        { AnchorDemo = epoch; AnchorReal = realNow; Rate = 1.0; Running = true }

    /// Demo instant for a given real instant. Pure in (clock, realNow) — that
    /// purity is the whole design, not an incidental style choice.
    let nowAt (c: DemoClock) (realNow: DateTimeOffset) =
        if not c.Running then c.AnchorDemo
        else c.AnchorDemo.AddSeconds((realNow - c.AnchorReal).TotalSeconds * c.Rate)

    /// Re-anchor at `realNow` leaving demo time exactly where it is. Every
    /// mutation below goes through this: changing `Rate` without re-anchoring
    /// retroactively rescales all elapsed history, so the countdown lurches.
    let private reanchor (c: DemoClock) (realNow: DateTimeOffset) =
        { c with AnchorDemo = nowAt c realNow; AnchorReal = realNow }

    let pause realNow c = { reanchor c realNow with Running = false }
    let resume realNow c = { reanchor c realNow with Running = true }
    let withRate rate realNow c = { reanchor c realNow with Rate = clampRate rate }

    /// Jumping always resumes.
    ///
    /// The alternative — jump while paused — freezes the countdown at the
    /// target and reads to an audience as an app that has hung. The plan
    /// required picking one behaviour rather than shipping both; this is it,
    /// encoded in the type so no caller can choose otherwise.
    ///
    /// Clamped forward to the epoch: seeded jobs are anchored there, and demo
    /// time before it would put all thirty of them in the future by a margin
    /// that makes the "in 8 min" countdown the demo opens on impossible.
    let jumpTo (target: DateTimeOffset) (realNow: DateTimeOffset) (c: DemoClock) =
        { c with
            AnchorDemo = (if target < epoch then epoch else target)
            AnchorReal = realNow
            Running = true }

    /// Signed on purpose: negative once the deadline has passed, so "overdue by
    /// 3:12" comes from the same expression as "in 3:12".
    let remaining (deadline: DateTimeOffset) (demoNow: DateTimeOffset) = deadline - demoNow
