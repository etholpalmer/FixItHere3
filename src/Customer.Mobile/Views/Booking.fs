module FixItHere.Customer.Views.Booking

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer
open FixItHere.Shared

/// One list, defined in Shared. The app used to keep its own, and the server
/// stored whatever string arrived — so a label the server could not resolve
/// would have booked a job at a time nobody chose.
let schedules = BookingSlot.options

let view (_model: Model) (providerId: int) (serviceId: int) =
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("When should they come?").font(size = 24.)
        for s in schedules do
            Button(s, BookJob (providerId, serviceId, s))
    }).padding(24.)
