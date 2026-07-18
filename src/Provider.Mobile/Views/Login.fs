module FixItHere.Provider.Views.Login

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let providers = [ "Mike's Plumbing"; "Joe Electric"; "Rapid Tire Repair"; "Elite HVAC" ]

let view (_model: Model) =
    (VStack(spacing = 12.) {
        Label("Which business is this?").font(size = 24.).centerTextHorizontal()
        for name in providers do
            Button(name, SelectProvider name)
    }).padding(24.)
