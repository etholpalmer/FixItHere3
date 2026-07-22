namespace FixItHere.Shared

open System

/// Turning "when should they come?" into an actual instant.
///
/// Booked jobs used to store the literal string `"Now"` in `ScheduledFor`.
/// Nothing in the system knew what time it was, so there was nothing to count
/// down to and nothing that could be late — the two features this phase is
/// built around were both blocked on this one field being a label instead of a
/// time.
///
/// Lives in `Shared` because the server resolves the label at booking and the
/// apps render the same options: two lists of slots that must agree is two
/// lists that eventually will not.
module BookingSlot =

    /// A provider cannot teleport. "Now" means "as soon as someone can get
    /// here", and twelve minutes is what that looks like — an instant arrival
    /// reads as fake faster than a slow one reads as broken.
    let asapLead = TimeSpan.FromMinutes 12.0

    /// The morning slot both day-based options land on.
    let private morningHour = 9

    /// Ordered, and the single source of truth for the booking screen. Adding
    /// an option here without teaching `tryResolve` about it makes the booking
    /// fail loudly at the API rather than silently booking the wrong time.
    let options = [ "Now"; "In 30 minutes"; "Tomorrow morning"; "Saturday morning" ]

    let private atMorning (d: DateTimeOffset) =
        DateTimeOffset(d.Year, d.Month, d.Day, morningHour, 0, 0, d.Offset)

    /// `None` for an unrecognised label, on purpose. Defaulting an unknown slot
    /// to "as soon as possible" would turn a typo into a job booked twelve
    /// minutes out — a wrong answer that looks like a right one.
    let tryResolve (label: string) (demoNow: DateTimeOffset) : DateTimeOffset option =
        match label with
        | "Now" -> Some (demoNow + asapLead)
        | "In 30 minutes" -> Some (demoNow.AddMinutes 30.0)
        | "Tomorrow morning" -> Some (atMorning (demoNow.AddDays 1.0))
        | "Saturday morning" ->
            // Always a *future* Saturday: booking one in the past is worse than
            // booking it a week out.
            let daysAhead =
                let raw = (int DayOfWeek.Saturday - int demoNow.DayOfWeek + 7) % 7
                if raw = 0 then 7 else raw
            Some (atMorning (demoNow.AddDays(float daysAhead)))
        | _ -> None

    /// What the provider's screen calls the slot once it is a real instant.
    let describe (start: DateTimeOffset) (demoNow: DateTimeOffset) =
        let days = (start.Date - demoNow.Date).Days
        let time = start.ToString("h:mm tt", Globalization.CultureInfo.InvariantCulture)
        match days with
        | 0 -> sprintf "Today, %s" time
        | 1 -> sprintf "Tomorrow, %s" time
        | d when d < 7 -> sprintf "%s, %s" (start.DayOfWeek.ToString()) time
        | _ -> start.ToString("d MMM, h:mm tt", Globalization.CultureInfo.InvariantCulture)
