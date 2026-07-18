module FixItHere.Customer.Views.Catalog

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (model: Model) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("What do you need?").font(size = 24.)
        for s in model.Services do
            Button(s.Name, Navigate (ProviderList s.Id))
    }).padding(24.)
