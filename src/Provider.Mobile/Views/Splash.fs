module FixItHere.Provider.Views.Splash

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let view (_model: Model) =
    (VStack(spacing = 8.) {
        Label("FixItHere").font(size = 42., attributes = Microsoft.Maui.Controls.FontAttributes.Bold).centerTextHorizontal()
        Label("Provider companion").centerTextHorizontal()
    }).centerVertical().padding(24.)
