module FixItHere.Provider.Views.Home

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
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
            for j in model.Jobs |> List.filter (fun j -> j.State = "Scheduled") do
                Button(sprintf "#%d %s — %s @ %s" j.Id j.ServiceName j.CustomerName j.Address,
                       Navigate (JobDetail j.Id))
        else
            Label("Go Online to see available jobs")
    }).padding(24.)
