module FixItHere.Provider.Views.ActiveJob

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared

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
                    Label(sprintf "%s — %s" job.CustomerName job.Address)
                    Label(sprintf "$%M" job.Price)
                }).gridRow(0)
                WebView(HtmlWebViewSource(Html = MapHtml.render Config.baseUrl job.Lat job.Lng job.ProviderId)).gridRow(1)
                (HStack(spacing = 8.) {
                    match actionButton job with
                    | Some (label, msg) -> Button(label, msg).background(Microsoft.Maui.Graphics.Colors.SeaGreen)
                    | None -> ()
                    Button("Chat", Navigate (Chat job.Id))
                    Button("Call", StartFakeCall)
                }).gridRow(2)
            }).padding(12.)
        )
