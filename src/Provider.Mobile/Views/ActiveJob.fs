module FixItHere.Provider.Views.ActiveJob

open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider

let private statusLine (state: string) =
    match state with
    | "Scheduled" -> "Ready to head out"
    | "EnRoute" -> "You're on the way"
    | "Arrived" -> "You have arrived"
    | "InProgress" -> "Work in progress"
    | "Completed" -> "Job complete!"
    | s -> s

/// The single state-driven next action for this job (spec: one button, driven by job state).
let private actionButton (j: FixItHere.Shared.Dtos.JobDto) =
    match j.State with
    | "Scheduled" -> Some ("Depart", Depart j.Id)
    | "EnRoute" -> Some ("Arrived", MarkArrived j.Id)
    | "Arrived" -> Some ("Start Work", BeginWork j.Id)
    | "InProgress" -> Some ("Complete", FinishWork j.Id)
    | _ -> None

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
