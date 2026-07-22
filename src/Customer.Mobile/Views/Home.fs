module FixItHere.Customer.Views.Home

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer
open FixItHere.Shared

let private nonTerminal (j: FixItHere.Shared.Dtos.JobDto) =
    j.State <> "Closed" && j.State <> "Cancelled"

let private statusOf (state: string) =
    JobStateCodec.tryParse state
    |> Option.map JobStatus.forCustomer
    |> Option.defaultValue "Checking status…"

/// Urgency drives colour. A deadline that has passed must not look like one
/// comfortably ahead — that is the entire reason `Countdown` carries a rank
/// rather than just a string.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Microsoft.Maui.Graphics.Color.FromRgb(0xB0, 0x2A, 0x2A)
    | Urgency.Urgent -> Microsoft.Maui.Graphics.Color.FromRgb(0x9A, 0x5B, 0x0A)
    | Urgency.Soon -> Microsoft.Maui.Graphics.Color.FromRgb(0x2B, 0x4D, 0x8A)
    | Urgency.Calm -> Microsoft.Maui.Graphics.Color.FromRgb(0x3A, 0x3A, 0x42)


let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    ScrollView(
     (VStack(spacing = 12.) {
        Label(sprintf "Hi, %s" name).font(size = 28.)
        Button("Book a New Service", Navigate Catalog)
        Label("Your active jobs").font(size = 18.)
        // Soonest first. The list opens on what is about to happen, which is
        // also the job whose countdown is worth watching.
        //
        // Flat, not a VStack per row: Fabulous CE rejects a nested VStack
        // inside a `for` in a CE (FS0792), a trap this codebase has hit before.
        for j in model.Jobs |> List.filter nonTerminal |> List.sortBy (fun j -> j.PromisedStart) do
            Button(sprintf "#%d %s — %s" j.Id j.ServiceName j.ProviderName,
                   Navigate (Tracking j.Id))
            match countdownFor model j with
            | Some c ->
                Label(sprintf "%s · %s %s" (statusOf j.State) c.Label c.Value)
                    .font(size = 12.)
                    .textColor(urgencyColor c.Urgency)
            | None ->
                Label(statusOf j.State).font(size = 12.)
     }).padding(24.))
