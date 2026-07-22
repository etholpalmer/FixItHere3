module FixItHere.Customer.Views.ProviderProfile

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer
open FixItHere.Shared

let view (model: Model) (providerId: int) =
    match model.Providers |> List.tryFind (fun p -> p.Id = providerId) with
    | None ->
        // Both arms scroll so they share a type; a match whose branches return
        // different widget types will not compile.
        ScrollView(
         (VStack(spacing = 12.) { Button("← Back", GoBack); Label("Provider not found") }).padding(24.))
    | Some p ->
        ScrollView(
         (VStack(spacing = 12.) {
            Button("← Back", GoBack)
            Label(p.BusinessName).font(size = 28.)
            Label(sprintf "%s — %s" p.ServiceName p.Vehicle)
            Label(sprintf "★ %.1f (%d ratings)" p.Rating p.RatingCount)
            Button("Book", Navigate (Booking (p.Id, p.ServiceId)))
            Label("Recent feedback").font(size = 18.)
            for r in model.ProfileRatings |> List.truncate 5 do
                // A review with no author and no date reads as filler.
                VStack(spacing = 0.) {
                    Label(sprintf "%s  %s · %s"
                            (String.replicate r.Stars "★")
                            (Format.displayName r.RaterName)
                            (Format.shortDate r.CreatedAt)).font(size = 12.)
                    Label(r.Comment)
                }
         }).padding(24.))
