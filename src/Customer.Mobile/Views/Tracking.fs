module FixItHere.Customer.Views.Tracking

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer

let private statusLine (state: string) =
    match state with
    | "Scheduled" -> "Waiting for provider to head out…"
    | "EnRoute" -> "Your provider is on the way"
    | "Arrived" -> "Your provider has arrived"
    | "InProgress" -> "Work in progress"
    | "Completed" -> "Job complete!"
    | s -> s

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None -> AnyView((VStack(spacing = 12.) { Button("← Back", GoBack); Label("Job not found") }).padding(24.))
    | Some job ->
        let etaLine =
            match model.ProviderPositions.TryFind job.ProviderId with
            | Some pos ->
                let km = Geo.distanceKm pos (job.Lat, job.Lng)
                sprintf "%.1f km away — ETA ~%d min" km (max 1 (int (km / 40.0 * 60.0)))
            | None -> "Locating provider…"
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto ]) {
                (VStack(spacing = 4.) {
                    Button("← Back", GoBack)
                    Label(statusLine job.State).font(size = 20.)
                    Label(sprintf "%s — %s ($%M)" job.ProviderName job.ServiceName job.Price)
                    Label(etaLine)
                }).gridRow(0)
                WebView(HtmlWebViewSource(Html = MapHtml.render Config.baseUrl job.Lat job.Lng job.ProviderId)).gridRow(1)
                (HStack(spacing = 8.) {
                    Button("Call", StartFakeCall)
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Cancel Job", CancelActiveJob job.Id)
                }).gridRow(2)
            }).padding(12.)
        )
