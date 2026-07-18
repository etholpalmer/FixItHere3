module FixItHere.Customer.Views.ProviderList

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("Nearby providers").font(size = 24.)
        for p in model.Providers do
            let km = Geo.distanceKm model.MyLocation (p.Lat, p.Lng)
            let dot = if p.Online then "●" else "○"
            Button(sprintf "%s %s  ★%.1f (%d)  %.1f km" dot p.BusinessName p.Rating p.RatingCount km,
                   Navigate (ProviderProfile p.Id))
    }).padding(24.)
