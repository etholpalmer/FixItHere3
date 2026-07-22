namespace FixItHere.Shared

open System

/// How long it takes to get there.
///
/// The unit is the whole point. ETA used to be computed inline in the customer's
/// tracking view as `km / 40.0 * 60.0` **real** minutes, while the countdown
/// beside it runs on demo time. At 1x nobody notices; at 60x the countdown
/// sprints to zero while the ETA barely moves and the car crawls — the two most
/// watched numbers on the demo's centrepiece screen openly contradicting each
/// other.
///
/// Everything here is in *demo* minutes. Converting to real seconds is the
/// clock's job, and only the thing physically pacing an animation needs to.
module Travel =

    /// Door-to-door average across the GTA, not free-flow highway speed.
    /// 40 km/h was optimistic enough to produce ETAs a Toronto audience would
    /// laugh at; this includes lights, turns and parking.
    let averageKmh = 32.0

    /// Never zero. "ETA 0 min" for a provider who is visibly still moving reads
    /// as a broken readout rather than an imminent arrival.
    let minMinutes = 1.0

    let minutesFor (km: float) =
        if Double.IsNaN km || km <= 0.0 then minMinutes
        else max minMinutes (km / averageKmh * 60.0)

    let durationFor (km: float) = TimeSpan.FromMinutes(minutesFor km)

    /// When someone this far away has to leave to arrive on time — the number
    /// that actually changes a provider's behaviour, and so the one their
    /// countdown shows while a job is still Scheduled.
    let departBy (promisedStart: DateTimeOffset) (km: float) = promisedStart - durationFor km

    /// "6.2 km away · ETA 12 min". One renderer, because the customer's
    /// tracking screen and the provider's active job must not phrase the same
    /// fact two ways.
    let describe (km: float) =
        sprintf "%.1f km away · ETA %s" km (Format.duration (int (round (minutesFor km))))
