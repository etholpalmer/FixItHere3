module FixItHere.Customer.Views.Tracking

open Microsoft.Maui
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

/// Mapped onto the shared token set so this screen and Home's job list agree
/// on what each colour means — a customer bouncing between the two must never
/// see the same word paired with two different colours.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Theme.danger
    | Urgency.Urgent -> Theme.warning
    | Urgency.Soon -> Theme.calm
    | Urgency.Calm -> Theme.inkMuted

let private statusLine (state: string) (cancelledBy: string) =
    match JobStateCodec.tryParse state with
    | Some Cancelled -> JobStatus.cancelledBy ActorRole.Customer (ActorRole.ofWire cancelledBy)
    | Some s -> JobStatus.forCustomer s
    | None -> "Checking status…"

/// The status card: who, what, and the one number this whole screen exists
/// to answer. Its own function so the `Border` wrapping it, and the `match`
/// on the countdown inside it, resolve independently of the outer `Grid`.
let private statusCard (model: Model) (job: FixItHere.Shared.Dtos.JobDto) (etaLine: string) =
    Border(
        VStack(spacing = Theme.Space.xs) {
            Button("‹", GoBack)
                .font(size = Theme.Font.title1)
                .textColor(Theme.brand)
                .width(Theme.touchTarget).height(Theme.touchTarget)

            Label(statusLine job.State job.CancelledBy)
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)

            // The countdown is the headline, not a footnote: it is the
            // number the customer is actually here for — large-title scale,
            // so it reads in under a second, one-handed, from across a room.
            // One Label rather than a nested HStack — Fabulous CE rejects
            // nesting here.
            match countdownFor model job with
            | Some c ->
                Label(sprintf "%s %s" c.Label c.Value)
                    .font(size = Theme.Font.largeTitle, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
            | None -> ()

            Label(sprintf "%s — %s (%s)" job.ProviderName job.ServiceName (Format.money job.Price))
                .font(size = Theme.Font.subhead)
                .textColor(Theme.ink)

            Label(etaLine)
                .font(size = Theme.Font.footnote)
                .textColor(Theme.inkMuted)
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Theme.Space.lg)

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None ->
        AnyView(
            (VStack(spacing = Theme.Space.md) {
                Button("‹", GoBack).font(size = Theme.Font.title1).textColor(Theme.brand).width(Theme.touchTarget).height(Theme.touchTarget)
                Label("Job not found").font(size = Theme.Font.body).textColor(Theme.inkMuted)
            }).padding(Theme.screenMargin))
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
                (statusCard model job etaLine)
                    .gridRow(0)

                // The answer bar. Present only while a proposal is live, and
                // directly under the countdown that is running out on it —
                // putting it below the map would hide the decision behind a
                // scroll on the one screen where time is the point.
                (HStack(spacing = Theme.Space.sm) {
                    match sched.Pending with
                    | Some p ->
                        Label(sprintf "New time: %s" (Format.clockTime (p.ProposedStart.ToString "o")))
                            .font(size = Theme.Font.subhead)
                            .textColor(Theme.ink)
                            .centerVertical()
                        Button("Accept", AnswerReschedule (job.Id, true))
                            .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                            .textColor(Theme.onBrand)
                            .background(Theme.brand)
                        Button("Decline", AnswerReschedule (job.Id, false))
                            .font(size = Theme.Font.callout)
                            .textColor(Theme.inkMuted)
                    | None -> ()
                })
                    .padding(Thickness(0., Theme.Space.xs, 0., Theme.Space.xs))
                    .gridRow(1)

                WebView(MapCache.source Config.baseUrl job.Lat job.Lng job.ProviderId).gridRow(2)

                (HStack(spacing = Theme.Space.sm) {
                    Button("Call", StartFakeCall)
                        .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                        .textColor(Theme.brand)
                    Button("Chat", Navigate (Chat job.Id))
                        .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                        .textColor(Theme.brand)
                    // Asks first. Cancelling is irreversible and was one tap
                    // away, on a screen an investor is handed to poke at.
                    if model.ConfirmingCancel = Some job.Id then
                        Button("Yes, cancel", CancelActiveJob job.Id)
                            .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                            .textColor(Theme.danger)
                        Button("Keep it", DismissCancel)
                            .font(size = Theme.Font.callout)
                            .textColor(Theme.inkMuted)
                    else
                        Button("Cancel Job", RequestCancel job.Id)
                            .font(size = Theme.Font.callout)
                            .textColor(Theme.inkMuted)
                    // Appears only once the grace window has actually elapsed.
                    // Escalation is mechanical, not scripted: the same clock
                    // that runs the countdown decides when this exists.
                    if canReportNoShow then
                        Button("Report no-show", ReportNoShow job.Id)
                            .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                            .textColor(Theme.danger)
                })
                    .padding(Thickness(0., Theme.Space.sm, 0., 0.))
                    .gridRow(3)
            }).padding(Theme.Space.md))
