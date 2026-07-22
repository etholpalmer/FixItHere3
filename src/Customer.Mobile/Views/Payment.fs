module FixItHere.Customer.Views.Payment

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared
open FixItHere.Shared.Dtos

/// Its own function, matching the other screens in this funnel.
let private header (title: string) =
    Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto ]) {
        Button("‹", GoBack)
            .font(size = Theme.Font.title1)
            .textColor(Theme.brand)
            .width(Theme.touchTarget).height(Theme.touchTarget)
            .gridColumn(0)
        Label(title)
            .font(size = Theme.Font.title1, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .gridColumn(1)
            .centerVertical()
    }

/// One receipt row — a label and its value, in the same two-column language
/// as every card row in this funnel (Home's jobRow, Booking's slotRow).
let private line (label: string) (value: string) =
    Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto ]) {
        Label(label)
            .font(size = Theme.Font.subhead)
            .textColor(Theme.inkMuted)
            .gridColumn(0)
        Label(value)
            .font(size = Theme.Font.subhead)
            .textColor(Theme.ink)
            .gridColumn(1)
    }

/// The one row that isn't a subtotal on the way to somewhere else — it gets
/// the weight the rest of the receipt deliberately doesn't.
let private totalLine (label: string) (value: string) =
    Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto ]) {
        Label(label)
            .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .gridColumn(0)
        Label(value)
            .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .gridColumn(1)
    }

/// The receipt, as one bordered card — the same surface/hairline language as
/// every grouped card in this funnel. Built as its own function, not inlined:
/// that is what keeps the `VStack` of rows safely inside the `Border` here,
/// the same trick `jobRow` and `slotRow` use to stay clear of FS0792.
let private receiptCard (r: PaymentResult) =
    Border(
        VStack(spacing = Theme.Space.sm) {
            Label("Receipt")
                .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                .textColor(Theme.inkMuted)
            line "Call-out fee" (Format.money r.CallOutFee)
            line (sprintf "Labour (%s)" (Format.duration r.LabourMinutes)) (Format.money r.LabourAmount)
            line "Subtotal" (Format.money r.Subtotal)
            line "HST (13%)" (Format.money r.Tax)
            totalLine "Total" (Format.money r.Amount)
            line "Paid with" r.Method
            Label(sprintf "Job #%d · %s" r.JobId r.Status)
                .font(size = Theme.Font.caption)
                .textColor(Theme.inkMuted)
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.lg, Theme.Space.md))

/// The one action this screen leads to. Same filled Border-around-Button
/// shape as Home's CTA and ProviderProfile's booking button, so "the button
/// you're meant to press" reads the same everywhere in the app.
let private primaryButton (text: string) (msg: Msg) =
    Border(
        Button(text, msg)
            .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
            .textColor(Theme.onBrand))
        .stroke(Theme.brand)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.brand)
        .padding(Thickness(Theme.Space.xs, Theme.Space.xs, Theme.Space.xs, Theme.Space.xs))

let view (model: Model) (jobId: int) =
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        header "Receipt"

        match model.PaymentResult with
        | None ->
            // Same calm-under-load register as the tracking screen: a
            // steady label and one indicator, not a spinner that thrashes.
            Label("Processing payment…")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .centerTextHorizontal()
            ActivityIndicator(true)
                .color(Theme.brand)
                .centerHorizontal()
        | Some r ->
            // The hero: a confident "paid" state and the amount, both
            // scaled up so the moment carries without decoration.
            Label("✓ Paid")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.success)
                .centerTextHorizontal()
            Label(Format.money r.Amount)
                .font(size = Theme.Font.largeTitle, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .centerTextHorizontal()

            receiptCard r

            primaryButton "Rate your experience" (Navigate (Rating jobId))
     }).padding(Theme.screenMargin))
