module FixItHere.Customer.Views.Booking

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let schedules = [ "Now"; "30 minutes"; "Tomorrow"; "Saturday" ]

let view (_model: Model) (providerId: int) (serviceId: int) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("When should they come?").font(size = 24.)
        for s in schedules do
            Button(s, BookJob (providerId, serviceId, s))
    }).padding(24.)
