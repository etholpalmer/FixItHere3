module FixItHere.Provider.Views.RateCustomer

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider

/// Its own function, matching the header language the rest of the funnel
/// uses (Chat, Payment).
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

/// One star. Large and full touch-target, so five of them read as one
/// satisfying tappable row rather than five cramped glyphs — same language
/// as the customer Rating screen.
let private star (filled: bool) (rank: int) =
    Button((if filled then "★" else "☆"), StarsChanged rank)
        .font(size = Theme.Font.largeTitle)
        .textColor(if filled then Theme.brand else Theme.surfaceEdge)
        .width(Theme.touchTarget).height(Theme.touchTarget)

/// Same composer-field language as Chat: a heavier stroke than the hairline
/// cards use, because a text field is a control someone is about to act on,
/// not a fact they are reading. Its own function so the Border-around-Entry
/// stays clear of FS0792.
let private commentField (comment: string) =
    Border(
        Entry(comment, RatingCommentChanged)
            .font(size = Theme.Font.body)
            .textColor(Theme.ink)
            .placeholder("Add a comment (optional)")
            .placeholderColor(Theme.inkMuted))
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.page)
        .padding(Thickness(Theme.Space.md, Theme.Space.xs, Theme.Space.md, Theme.Space.xs))

/// Filled Border-around-Button — the same primary-action shape as every
/// other screen's CTA, so "the button you're meant to press" reads the
/// same everywhere in the app.
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
        header "Rate your customer"

        Label("Tap a star to rate your customer")
            .font(size = Theme.Font.subhead)
            .textColor(Theme.inkMuted)
            .centerTextHorizontal()

        (HStack(spacing = Theme.Space.sm) {
            for i in 1 .. 5 do
                star (i <= model.RatingStars) i
        }).centerHorizontal()

        commentField model.RatingComment

        primaryButton "Submit" (SubmitRating (jobId, model.RatingStars, model.RatingComment))
     }).padding(Theme.screenMargin))
