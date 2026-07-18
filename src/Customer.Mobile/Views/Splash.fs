module FixItHere.Customer.Views.Splash

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let view (_model: Model) =
    (VStack(spacing = 8.) {
        Label("FixItHere").font(size = 42., attributes = Microsoft.Maui.Controls.FontAttributes.Bold).centerTextHorizontal()
        Label("Mobile services, wherever you are").centerTextHorizontal()
    }).centerVertical().padding(24.)
