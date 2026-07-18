module FixItHere.Customer.Views.Home

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let private nonTerminal (j: FixItHere.Shared.Dtos.JobDto) =
    j.State <> "Closed" && j.State <> "Cancelled"

let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    (VStack(spacing = 12.) {
        Label(sprintf "Hi, %s" name).font(size = 28.)
        Button("Book a New Service", Navigate Catalog)
        Label("Your active jobs").font(size = 18.)
        for j in model.Jobs |> List.filter nonTerminal do
            Button(sprintf "#%d %s — %s (%s)" j.Id j.ServiceName j.ProviderName j.State,
                   Navigate (Tracking j.Id))
        Button("Developer Settings", Navigate DevSettings)
    }).padding(24.)
