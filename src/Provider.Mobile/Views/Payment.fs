module FixItHere.Provider.Views.Payment

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let view (model: Model) (jobId: int) =
    (VStack(spacing = 16.) {
        match model.PaymentResult with
        | None ->
            Label("Payment Authorized").font(size = 28.).centerTextHorizontal()
            ActivityIndicator(true)
            Label("Processing…").centerTextHorizontal()
        | Some r ->
            let receipt =
                (VStack(spacing = 4.) {
                    Label("— Receipt —").centerTextHorizontal()
                    Label(sprintf "Job #%d" r.JobId).centerTextHorizontal()
                    Label(sprintf "Status: %s" r.Status).centerTextHorizontal()
                })

            Label("✓ Payment Received").font(size = 28.).centerTextHorizontal()
            Label(sprintf "$%.2f" (float r.Amount)).font(size = 40.).centerTextHorizontal()
            receipt
            Button("Rate customer", Navigate (RateCustomer jobId))
    }).centerVertical().padding(24.)
