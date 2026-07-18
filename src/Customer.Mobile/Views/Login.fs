module FixItHere.Customer.Views.Login

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let customers = [ "John"; "Mary"; "Steve"; "Susan"; "Bob" ]

let view (_model: Model) =
    (VStack(spacing = 12.) {
        Label("Who's booking today?").font(size = 24.).centerTextHorizontal()
        for name in customers do
            Button(name, SelectCustomer name)
    }).padding(24.)
