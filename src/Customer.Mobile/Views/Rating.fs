module FixItHere.Customer.Views.Rating

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer

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

/// One star. Large and full touch-target, so five of them read as one
/// satisfying tappable row rather than five cramped glyphs.
let private star (filled: bool) (rank: int) =
    Button((if filled then "★" else "☆"), StarsChanged rank)
        .font(size = Theme.Font.largeTitle)
        .textColor(if filled then Theme.brand else Theme.surfaceEdge)
        .width(Theme.touchTarget).height(Theme.touchTarget)

/// Same composer-field language as Chat: a heavier stroke than the hairline
/// cards use, because a text field is a control someone is about to act on,
/// not a fact they are reading. Its own function — inlining a `Border`
/// wrapping an `Entry` directly inside the outer `VStack` here (unlike
/// Chat's, whose composer lives inside a `Grid`) hits FS0792.
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

/// Filled Border-around-Button — the same primary-action shape as Home's
/// CTA, ProviderProfile's booking button, and Chat's Send, so "the button
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
        header "How was it?"

        Label("Tap a star to rate your provider")
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
