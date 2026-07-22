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
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
                (VStack(spacing = 4.) {
                    Button("← Back", GoBack)
                    Label(statusLine job.State).font(size = 20.)
                    Label(sprintf "%s — %s ($%M)" job.ProviderName job.ServiceName job.Price)
                    Label(etaLine)
                }).gridRow(0)
                WebView(HtmlWebViewSource(Html = MapCache.html Config.baseUrl job.Lat job.Lng job.ProviderId)).gridRow(1)
                (HStack(spacing = 8.) {
                    Button("Call", StartFakeCall)
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Cancel Job", CancelActiveJob job.Id)
                }).gridRow(2)
            }).padding(12.)
        )
