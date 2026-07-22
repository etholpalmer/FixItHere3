module FixItHere.Customer.Views.Payment

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer
open FixItHere.Shared

let view (model: Model) (jobId: int) =
    ScrollView(
     (VStack(spacing = 16.) {
        match model.PaymentResult with
        | None ->
            Label("Payment Authorized").font(size = 28.).centerTextHorizontal()
            ActivityIndicator(true)
            Label("Processing…").centerTextHorizontal()
        | Some r ->
            let line (label: string) (value: string) =
                (Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto ]) {
                    Label(label).gridColumn(0)
                    Label(value).gridColumn(1)
                })

            Label("✓ Paid").font(size = 28.).centerTextHorizontal()
            Label(Format.money r.Amount).font(size = 40.).centerTextHorizontal()
            VStack(spacing = 6.) {
                Label("Receipt").font(size = 16.)
                line "Call-out fee" (Format.money r.CallOutFee)
                line (sprintf "Labour (%s)" (Format.duration r.LabourMinutes)) (Format.money r.LabourAmount)
                line "Subtotal" (Format.money r.Subtotal)
                line "HST (13%)" (Format.money r.Tax)
                line "Total" (Format.money r.Amount)
                line "Paid with" r.Method
                Label(sprintf "Job #%d · %s" r.JobId r.Status).font(size = 11.)
            }
            Button("Rate your experience", Navigate (Rating jobId))
     }).centerVertical().padding(24.))
