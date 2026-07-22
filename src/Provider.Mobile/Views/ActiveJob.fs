module FixItHere.Provider.Views.ActiveJob

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared


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

let private statusLine (state: string) (cancelledBy: string) =
    match JobStateCodec.tryParse state with
    | Some Cancelled -> JobStatus.cancelledBy ActorRole.Provider (ActorRole.ofWire cancelledBy)
    | Some s -> JobStatus.forProvider s
    | None -> "Checking status…"

/// The single state-driven next action for this job (spec: one button, driven
/// by job state). Shared decides *which* transition; this only maps it to the
/// app's own Msg, so the button label and the event it fires cannot disagree.
let private actionButton (j: FixItHere.Shared.Dtos.JobDto) =
    JobStateCodec.tryParse j.State
    |> Option.bind JobStatus.nextProviderAction
    |> Option.map (fun (label, ev) ->
        let msg =
            match ev with
            | DepartEnRoute -> Depart j.Id
            | Arrive -> MarkArrived j.Id
            | StartWork -> BeginWork j.Id
            | CompleteWork -> FinishWork j.Id
            | Accepted | RateAndClose | Cancel | MarkNoShow -> Depart j.Id
        label, msg)

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None -> AnyView((VStack(spacing = 12.) { Button("← Back", GoBack); Label("Job not found") }).padding(24.))
    | Some job ->
        let sched : Reschedule = rescheduleOf job
        let awaitingArrival =
            JobStateCodec.tryParse job.State
            |> Option.map JobStatus.awaitsArrival
            |> Option.defaultValue false
        let proposalPending = sched.Pending.IsSome
        let lateControlsVisible = awaitingArrival && not proposalPending
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto; Auto ]) {
                (VStack(spacing = 4.) {
                    Button("← Back", GoBack)
                    Label(statusLine job.State job.CancelledBy).font(size = 20.)
                    // The countdown is the headline, not a footnote: it is
                    // the number this screen exists to show. One Label rather
                    // than a nested HStack — Fabulous CE rejects nesting here.
                    match countdownFor model job with
                    | Some c ->
                        Label(sprintf "%s %s" c.Label c.Value)
                            .font(size = 22.)
                            .textColor(urgencyColor c.Urgency)
                    | None -> ()
                    Label(sprintf "%s — %s" job.CustomerName job.Address)
                    Label(Format.money job.Price)
                }).gridRow(0)
                WebView(MapCache.source Config.baseUrl job.Lat job.Lng job.ProviderId).gridRow(1)
                // Offered only while an arrival is still ahead of us, and only
                // when nothing is already pending — the server refuses a second
                // proposal, so a button that could raise one would be a lie.
                //
                // One flat HStack: Fabulous CE rejects nested layouts here.
                (HStack(spacing = 8.) {
                    if lateControlsVisible then
                        Label("Running late?")
                            .font(size = 13.)
                            .textColor(Theme.inkMuted)
                            .centerVertical()
                        for mins in [ 10; 15; 30 ] do
                            Button(sprintf "+%d" mins, ProposeDelay (job.Id, mins))
                    elif proposalPending then
                        Label("Waiting for the customer to answer")
                            .font(size = 13.)
                            .textColor(Theme.warning)
                })
                    .gridRow(2)

                (HStack(spacing = 8.) {
                    match actionButton job with
                    | Some (label, msg) -> Button(label, msg).background(Microsoft.Maui.Graphics.Colors.SeaGreen)
                    | None -> ()
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Call", StartFakeCall)
                    if model.ConfirmingCancel = Some job.Id then
                        Button("Yes, cancel", CancelJob job.Id).textColor(Theme.danger)
                        Button("Keep it", DismissCancel)
                    else
                        Button("Cancel", RequestCancel job.Id).textColor(Theme.danger)
                }).gridRow(3)
            }).padding(12.)
        )
