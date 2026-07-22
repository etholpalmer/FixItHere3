/// Presentation helpers shared by both apps.
///
/// These live in `Shared` rather than `ClientShared` for one reason: `Shared` is
/// a project reference, so `Shared.Tests` can actually test them, whereas
/// `ClientShared` files are *linked* into each app and are therefore invisible
/// to every test project in the repo.
///
/// Every function here takes the ISO-8601 strings the DTOs carry and is total —
/// a malformed or empty timestamp returns "" rather than throwing, because a
/// blank line in a chat bubble is a cosmetic defect and an exception on a
/// tracking screen is a dead demo.
module FixItHere.Shared.Format

open System
open System.Globalization

let private tryParse (iso: string) =
    if String.IsNullOrWhiteSpace iso then None
    else
        match DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                                      DateTimeStyles.RoundtripKind) with
        | true, dt -> Some dt
        | _ -> None

/// "3:42 PM" — what sits beside a chat bubble.
///
/// Rendered in the instant's own offset, deliberately NOT converted to the
/// device's local zone. Every time in this product is a *demo* instant on a
/// fictional timeline anchored at the epoch; shifting it into the operator's
/// real timezone produced a screen where the countdown read 00:23 and the
/// proposed time beside it read 7:23 PM. Two views of the same moment that
/// disagree by four hours is exactly the kind of incoherence this build exists
/// to remove.
let clockTime (iso: string) =
    tryParse iso
    |> Option.map (fun d -> d.ToString("h:mm tt", CultureInfo.InvariantCulture))
    |> Option.defaultValue ""

/// "12 Jan" — what dates a review. Year-less on purpose: reviews inside the
/// last twelve months read as current, and a year makes stale data conspicuous.
let shortDate (iso: string) =
    tryParse iso
    |> Option.map (fun d -> d.ToString("d MMM", CultureInfo.InvariantCulture))
    |> Option.defaultValue ""

/// "Mary O." — a reviewer is a person, but a full surname on a public profile
/// is more exposure than a real product would print.
let displayName (fullName: string) =
    if String.IsNullOrWhiteSpace fullName then "Someone"
    else
        match fullName.Trim().Split(' ') with
        | [| single |] -> single
        | parts -> sprintf "%s %c." parts.[0] (Char.ToUpperInvariant parts.[parts.Length - 1].[0])

/// "$1,234.56" — one money formatter, so no screen invents its own.
let money (amount: decimal) =
    amount.ToString("C2", CultureInfo.GetCultureInfo "en-CA")

/// "1h 30m" / "45m" — durations read as durations, never as bare minute counts.
let duration (minutes: int) =
    if minutes < 60 then sprintf "%dm" minutes
    elif minutes % 60 = 0 then sprintf "%dh" (minutes / 60)
    else sprintf "%dh %dm" (minutes / 60) (minutes % 60)

/// "8:32" · "in 2h 14m" · "3:12 late". The only countdown renderer in the
/// product: two independent `statusLine` implementations already drifted in
/// this codebase, and a countdown that reads differently on the two phones
/// standing side by side is the exact tell this phase exists to remove.
let countdown (remaining: TimeSpan) =
    let abs = remaining.Duration()
    let body =
        if abs.TotalHours >= 1.0 then sprintf "%dh %02dm" (int abs.TotalHours) abs.Minutes
        else sprintf "%d:%02d" (int abs.TotalMinutes) abs.Seconds
    if remaining.Ticks < 0L then sprintf "%s late" body else body
