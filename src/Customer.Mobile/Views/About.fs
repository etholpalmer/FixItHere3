module FixItHere.Customer.Views.About

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer

/// Credit for one sample photo. The photos are openly licensed (CC0, which
/// needs no attribution) but crediting the makers is the decent thing — and a
/// real product has an About page, so its absence is itself a small tell.
type private Credit = { Title: string; Creator: string; Source: string }

let private credits =
    [ { Title = "Magnum P.I."; Creator = "Joe Folino ( LoopRunner )"; Source = "Flickr" }
      { Title = "ZAZ-965 engine bay"; Creator = "DL24"; Source = "Wikimedia" }
      { Title = "Outdoor cooling equipment at NERSC (4)"; Creator = "D Coetzee"; Source = "Flickr" }
      { Title = "Outdoor cooling equipment at NERSC (5)"; Creator = "D Coetzee"; Source = "Flickr" }
      { Title = "Unprofessional plumbing pipe repair"; Creator = "Syced"; Source = "Wikimedia" }
      { Title = "Plumbing pipes"; Creator = "Jesuririme"; Source = "Wikimedia" }
      { Title = "Residence service drop"; Creator = "Chetvorno"; Source = "Wikimedia" }
      { Title = "Ōtautahi mural, Christchurch."; Creator = "Bernard Spragg"; Source = "Flickr" }
      { Title = "Moving truck outside sunshine bay 1441 building"; Creator = "miamibrickell"; Source = "Flickr" }
      { Title = "bookshelf tools, cleaning supplies, labeled"; Creator = "Unknown"; Source = "Rawpixel" } ]

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

/// One credit, in the same bordered-row language as every card in the app.
/// Its own function so the inner layout stays clear of FS0792 in the loop.
let private creditRow (c: Credit) =
    Border(
        VStack(spacing = Theme.gapTight) {
            Label(c.Title)
                .font(size = Theme.Font.subhead, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .lineBreakMode(Microsoft.Maui.LineBreakMode.WordWrap)
            Label(sprintf "%s · %s · CC0" c.Creator c.Source)
                .font(size = Theme.Font.footnote)
                .textColor(Theme.inkMuted)
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.lg, Theme.Space.md))

let view (_model: Model) =
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        header "About"

        Label("FixItHere")
            .font(size = Theme.Font.title2, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
        Label("A prototype for a mobile-services marketplace — book a nearby provider, watch them travel to you, chat, pay, and rate.")
            .font(size = Theme.Font.subhead)
            .textColor(Theme.inkMuted)

        Label("Photo credits")
            .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .padding(Thickness(0., Theme.Space.sm, 0., 0.))
        Label("Sample photos are openly licensed under Creative Commons Zero (CC0) and sourced via Openverse. Attribution is not required for CC0, but the makers are credited here.")
            .font(size = Theme.Font.footnote)
            .textColor(Theme.inkMuted)

        for c in credits do
            creditRow c
     }).padding(Theme.screenMargin))
