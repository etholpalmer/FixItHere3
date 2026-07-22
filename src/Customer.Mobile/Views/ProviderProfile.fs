module FixItHere.Customer.Views.ProviderProfile

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// Its own function, not inlined into the outer `VStack` — matches the other
/// five screens in this funnel so the back chevron looks identical wherever
/// it appears.
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

/// The one action this screen exists to lead to. Same filled Border-around-
/// Button shape as Home's primary CTA and Chat's Send, so "the button you're
/// meant to press" reads the same everywhere in the app.
let private bookButton (msg: Msg) =
    Border(
        Button("Book this provider", msg)
            .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
            .textColor(Theme.onBrand))
        .stroke(Theme.brand)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.brand)
        .padding(Thickness(Theme.Space.xs, Theme.Space.xs, Theme.Space.xs, Theme.Space.xs))

/// One review. No author and no date read as filler; "Mary O. · 12 Jan"
/// reads as a person who actually used this provider.
let private reviewRow (stars: int) (author: string) (date: string) (comment: string) =
    Border(
        Grid(coldefs = [ Star ], rowdefs = [ Auto; Auto ]) {
            Label(sprintf "%s  %s · %s" (String.replicate stars "★") author date)
                .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                .textColor(Theme.brand)
                .gridRow(0)
            Label(comment)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.ink)
                .gridRow(1)
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.lg, Theme.Space.md))

let view (model: Model) (providerId: int) =
    match model.Providers |> List.tryFind (fun p -> p.Id = providerId) with
    | None ->
        // Both arms scroll so they share a type; a match whose branches
        // return different widget types will not compile.
        ScrollView(
         (VStack(spacing = Theme.Space.lg) {
            header "Provider"
            Label("Provider not found")
                .font(size = Theme.Font.body)
                .textColor(Theme.inkMuted)
         }).padding(Theme.screenMargin))
    | Some p ->
        ScrollView(
         (VStack(spacing = Theme.Space.lg) {
            header p.BusinessName

            Label(sprintf "%s · %s" p.ServiceName p.Vehicle)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)

            Label(Format.rating p.Rating p.RatingCount)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(if p.RatingCount = 0 then Theme.inkMuted else Theme.brand)

            bookButton (Navigate (Booking (p.Id, p.ServiceId)))

            Label("Recent feedback")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))

            if List.isEmpty model.ProfileRatings then
                Label("No reviews yet — this provider's first job could be yours.")
                    .font(size = Theme.Font.subhead)
                    .textColor(Theme.inkMuted)
            else
                for r in model.ProfileRatings |> List.truncate 5 do
                    reviewRow r.Stars (Format.displayName r.RaterName) (Format.shortDate r.CreatedAt) r.Comment
         }).padding(Theme.screenMargin))
