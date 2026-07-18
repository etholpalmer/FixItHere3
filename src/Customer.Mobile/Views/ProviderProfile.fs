module FixItHere.Customer.Views.ProviderProfile

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) (providerId: int) =
    match model.Providers |> List.tryFind (fun p -> p.Id = providerId) with
    | None ->
        (VStack(spacing = 12.) { Button("← Back", GoBack); Label("Provider not found") }).padding(24.)
    | Some p ->
        (VStack(spacing = 12.) {
            Button("← Back", GoBack)
            Label(p.BusinessName).font(size = 28.)
            Label(sprintf "%s — %s" p.ServiceName p.Vehicle)
            Label(sprintf "★ %.1f (%d ratings)" p.Rating p.RatingCount)
            Button("Book", Navigate (Booking (p.Id, p.ServiceId)))
            Label("Recent feedback").font(size = 18.)
            for r in model.ProfileRatings |> List.truncate 5 do
                Label(sprintf "★%d  %s" r.Stars r.Comment)
        }).padding(24.)
