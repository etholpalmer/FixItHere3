module FixItHere.Provider.Views.Payment

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider
open FixItHere.Shared

let view (model: Model) (jobId: int) =
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

            // The provider's headline number is their payout, not the customer's
            // total — showing the same figure on both screens is the tell that
            // there is no marketplace behind them.
            Label("✓ Payout").font(size = 28.).centerTextHorizontal()
            Label(Format.money r.ProviderPayout).font(size = 40.).centerTextHorizontal()
            VStack(spacing = 6.) {
                Label("Earnings").font(size = 16.)
                line "Call-out fee" (Format.money r.CallOutFee)
                line (sprintf "Labour (%s)" (Format.duration r.LabourMinutes)) (Format.money r.LabourAmount)
                line "Subtotal" (Format.money r.Subtotal)
                line "Platform fee (15%)" (sprintf "-%s" (Format.money r.PlatformFee))
                line "Your payout" (Format.money r.ProviderPayout)
                Label(sprintf "Job #%d · %s" r.JobId r.Status).font(size = 11.)
            }
            Button("Rate customer", Navigate (RateCustomer jobId))
    }).centerVertical().padding(24.)
