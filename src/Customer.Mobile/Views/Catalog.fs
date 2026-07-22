module FixItHere.Customer.Views.Catalog

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// One row per trade. A bare trade name read as a directory listing — no
/// price, no idea how long it takes, nothing to decide from. `FromPrice` and
/// `TypicalMinutes` were already on the wire and simply never reached glass.
let private serviceRow (msg: Msg) (title: string) (subtitle: string) =
    Border(
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto; Auto ]) {
            Label(title)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(0).gridRow(0)
            Label(subtitle)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
                .gridColumn(0).gridRow(1)
            Label("›")
                .font(size = Theme.Font.title2)
                .textColor(Theme.inkMuted)
                .gridColumn(1).gridRowSpan(2)
                .centerVertical()
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.lg, Theme.Space.md))
        .gestureRecognizers() { TapGestureRecognizer(msg) }

/// Inline header: this screen is a step down the funnel, not a tab root, so
/// it gets the back chevron rather than a large title. Its own function for
/// the same reason `serviceRow` is — a `Grid` yielded directly inside the
/// outer `VStack`'s CE (ahead of a `for`) is what actually trips FS0792 here,
/// not the `for` alone.
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
        header "What do you need?"

        for s in model.Services do
            serviceRow (Navigate (ProviderList s.Id)) s.Name
                (sprintf "from %s · ~%s" (Format.money s.FromPrice) (Format.duration s.TypicalMinutes))
     }).padding(Theme.screenMargin))
