module FixItHere.Customer.Views.ProviderList

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// One row per provider: name, then the three facts that actually decide it
/// — rating, distance, whether they are online right now. A list row, not a
/// button — the online dot and chevron are the only decoration, and the tap
/// target is the whole row.
let private providerRow (msg: Msg) (online: bool) (title: string) (subtitle: string) =
    Border(
        Grid(coldefs = [ Auto; Star; Auto ], rowdefs = [ Auto; Auto ]) {
            Label(if online then "●" else "○")
                .font(size = Theme.Font.caption)
                .textColor(if online then Theme.success else Theme.inkMuted)
                .gridColumn(0).gridRowSpan(2)
                .centerVertical()
            Label(title)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(1).gridRow(0)
            Label(subtitle)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
                .gridColumn(1).gridRow(1)
            Label("›")
                .font(size = Theme.Font.title2)
                .textColor(Theme.inkMuted)
                .gridColumn(2).gridRowSpan(2)
                .centerVertical()
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.lg, Theme.Space.md))
        .gestureRecognizers() { TapGestureRecognizer(msg) }

/// Its own function, not inlined into the outer `VStack` — a `Grid` yielded
/// directly ahead of the `for` below is what trips FS0792 here.
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

let view (model: Model) =
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        header "Nearby providers"

        for p in model.Providers do
            let km = Geo.distanceKm model.MyLocation (p.Lat, p.Lng)
            providerRow (Navigate (ProviderProfile p.Id)) p.Online p.BusinessName
                (sprintf "%s · %.1f km away" (Format.rating p.Rating p.RatingCount) km)
     }).padding(Theme.screenMargin))
