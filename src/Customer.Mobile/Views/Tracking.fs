module FixItHere.Customer.Views.Tracking

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// Copy comes from Shared, which matches on the JobState union rather than on
/// strings. The old version ended `| s -> s`, so a state without copy rendered
/// its own enum name — the user would have read "ProviderNoShow".

/// Memoised map *source object*, not just its HTML.
///
/// Caching the string was necessary and not sufficient: the view wrapped it in
/// `HtmlWebViewSource(Html = ...)`, constructing a brand-new object on every
/// render. With the 250 ms countdown pump that is four new sources a second,
/// and Fabulous re-sets a property whose value is a different reference — so
/// the WebView reloaded continuously and the map visibly flashed.
///
/// A reload also drops and re-opens the page's own SignalR connection, which is
/// why "no hub churn" was the wrong thing to measure: the page came back faster
/// than a dropped connection took to surface.
///
/// Handing back the same instance makes the diff a no-op. Keyed on what the map
/// actually depends on, so moving to a different job still rebuilds it.
module private MapCache =
    let private cache =
        System.Collections.Generic.Dictionary<struct (float * float * int), HtmlWebViewSource>()

    let source (baseUrl: string) (lat: float) (lng: float) (providerId: int) =
        let key = struct (lat, lng, providerId)
        match cache.TryGetValue key with
        | true, v -> v
        | _ ->
            let v = HtmlWebViewSource(Html = MapHtml.render baseUrl lat lng providerId)
            cache[key] <- v
            v
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Microsoft.Maui.Graphics.Color.FromRgb(0xB0, 0x2A, 0x2A)
    | Urgency.Urgent -> Microsoft.Maui.Graphics.Color.FromRgb(0x9A, 0x5B, 0x0A)
    | Urgency.Soon -> Microsoft.Maui.Graphics.Color.FromRgb(0x2B, 0x4D, 0x8A)
    | Urgency.Calm -> Microsoft.Maui.Graphics.Color.FromRgb(0x3A, 0x3A, 0x42)

let private statusLine (state: string) =
    match JobStateCodec.tryParse state with
    | Some s -> JobStatus.forCustomer s
    | None -> "Checking status…"

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None -> AnyView((VStack(spacing = 12.) { Button("← Back", GoBack); Label("Job not found") }).padding(24.))
    | Some job ->
        let etaLine =
            match model.ProviderPositions.TryFind job.ProviderId with
            | Some pos ->
                // Demo minutes, from Shared. Computed inline here as *real*
                // minutes, this was the number that contradicted the countdown
                // beside it the moment the clock ran faster than 1x.
                Travel.describe (Geo.distanceKm pos (job.Lat, job.Lng))
            | None -> "Locating provider…"
        let sched : Reschedule = rescheduleOf job
        // The no-show control follows the same rule the server enforces, so a
        // button can never offer something the API will refuse.
        let canReportNoShow =
            (JobStateCodec.tryParse job.State |> Option.map JobStatus.awaitsArrival |> Option.defaultValue false)
            && Reschedule.canReportNoShow model.DemoNow sched
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto; Star; Auto ]) {
                (VStack(spacing = 4.) {
                    Button("← Back", GoBack)
                    Label(statusLine job.State).font(size = 20.)
                    // The countdown is the headline, not a footnote: it is the
                    // number the customer is actually here for.
                    // The countdown is the headline, not a footnote: it is
                    // the number this screen exists to show. One Label rather
                    // than a nested HStack — Fabulous CE rejects nesting here.
                    match countdownFor model job with
                    | Some c ->
                        Label(sprintf "%s %s" c.Label c.Value)
                            .font(size = 22.)
                            .textColor(urgencyColor c.Urgency)
                    | None -> ()
                    Label(sprintf "%s — %s (%s)" job.ProviderName job.ServiceName (Format.money job.Price))
                    Label(etaLine).font(size = 12.)
                }).gridRow(0)
                // The answer bar. Present only while a proposal is live, and
                // directly under the countdown that is running out on it —
                // putting it below the map would hide the decision behind a
                // scroll on the one screen where time is the point.
                (HStack(spacing = 8.) {
                    match sched.Pending with
                    | Some p ->
                        Label(sprintf "New time: %s" (Format.clockTime (p.ProposedStart.ToString "o")))
                            .font(size = 15.)
                            .textColor(Theme.ink)
                            .centerVertical()
                        Button("Accept", AnswerReschedule (job.Id, true))
                            .textColor(Theme.onBrand)
                            .background(Theme.brand)
                        Button("Decline", AnswerReschedule (job.Id, false))
                    | None -> ()
                }).gridRow(1)

                WebView(MapCache.source Config.baseUrl job.Lat job.Lng job.ProviderId).gridRow(2)
                (HStack(spacing = 8.) {
                    Button("Call", StartFakeCall)
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Cancel Job", CancelActiveJob job.Id)
                    // Appears only once the grace window has actually elapsed.
                    // Escalation is mechanical, not scripted: the same clock
                    // that runs the countdown decides when this exists.
                    if canReportNoShow then
                        Button("Report no-show", ReportNoShow job.Id)
                            .textColor(Theme.danger)
                }).gridRow(3)
            }).padding(12.)
        )
