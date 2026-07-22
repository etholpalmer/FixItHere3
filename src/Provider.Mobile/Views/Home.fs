module FixItHere.Provider.Views.Home

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider
open FixItHere.Shared

/// Urgency drives colour. A departure time already passed must not look like
/// one comfortably ahead.
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
        Label(sprintf "%s" name).font(size = 28.)
        (HStack(spacing = 8.) {
            Label(if model.Online then "● Online" else "○ Offline").font(size = 18.)
            Button((if model.Online then "Go Offline" else "Go Online"), SetOnline (not model.Online))
        }).centerHorizontal()
        match activeJob model with
        | Some j ->
            Label("Active job").font(size = 18.)
            Button(sprintf "#%d %s — %s (%s)" j.Id j.ServiceName j.CustomerName j.State,
                   Navigate (ActiveJob j.Id))
        | None -> ()
        if model.Online then
            Label("Available jobs").font(size = 18.)
            // Soonest first, each with the number that decides whether to take
            // it. Flat rather than a VStack per row: Fabulous CE rejects a
            // nested VStack inside a `for` in a CE (FS0792).
            for j in model.Jobs
                     |> List.filter (fun j -> j.State = "Scheduled")
                     |> List.sortBy (fun j -> j.PromisedStart) do
                Button(sprintf "#%d %s — %s @ %s" j.Id j.ServiceName j.CustomerName j.Address,
                       Navigate (JobDetail j.Id))
                match countdownFor model j with
                | Some c ->
                    Label(sprintf "%s %s" c.Label c.Value)
                        .font(size = 12.)
                        .textColor(urgencyColor c.Urgency)
                | None -> ()
        else
            Label("Go Online to see available jobs")
     }).padding(24.))
