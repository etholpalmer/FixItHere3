module FixItHere.Provider.Views.ActiveJob

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared


/// Memoised map HTML.
///
/// Both tracking screens built this by calling `MapHtml.render` *inside* the
/// view function, and that WebView hosts its own SignalR connection. With a
/// 250 ms countdown tick now re-running the view four times a second, a fresh
/// string every render would mean Fabulous re-setting `Html` — reloading the
/// map and reconnecting its hub four times a second, on the one screen the
/// whole demo is watching.
///
/// Keyed on what the map actually depends on, so panning to a different job
/// still rebuilds it. A dictionary rather than a single slot because a user can
/// move between jobs and back.
module private MapCache =
    let private cache = System.Collections.Generic.Dictionary<struct (float * float * int), string>()

    let html (baseUrl: string) (lat: float) (lng: float) (providerId: int) =
        let key = struct (lat, lng, providerId)
        match cache.TryGetValue key with
        | true, v -> v
        | _ ->
            let v = MapHtml.render baseUrl lat lng providerId
            cache[key] <- v
            v


/// Urgency drives colour. A deadline that has passed must not look like one
/// comfortably ahead — that is the entire reason `Countdown` carries a rank
/// rather than just a string.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Microsoft.Maui.Graphics.Color.FromRgb(0xB0, 0x2A, 0x2A)
    | Urgency.Urgent -> Microsoft.Maui.Graphics.Color.FromRgb(0x9A, 0x5B, 0x0A)
    | Urgency.Soon -> Microsoft.Maui.Graphics.Color.FromRgb(0x2B, 0x4D, 0x8A)
    | Urgency.Calm -> Microsoft.Maui.Graphics.Color.FromRgb(0x3A, 0x3A, 0x42)

let private statusLine (state: string) =
    match JobStateCodec.tryParse state with
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
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
                (VStack(spacing = 4.) {
                    Button("← Back", GoBack)
                    Label(statusLine job.State).font(size = 20.)
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
                WebView(HtmlWebViewSource(Html = MapCache.html Config.baseUrl job.Lat job.Lng job.ProviderId)).gridRow(1)
                (HStack(spacing = 8.) {
                    match actionButton job with
                    | Some (label, msg) -> Button(label, msg).background(Microsoft.Maui.Graphics.Colors.SeaGreen)
                    | None -> ()
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Call", StartFakeCall)
                }).gridRow(2)
            }).padding(12.)
        )
