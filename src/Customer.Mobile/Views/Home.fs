module FixItHere.Customer.Views.Home

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

let private nonTerminal (j: FixItHere.Shared.Dtos.JobDto) =
    j.State <> "Closed" && j.State <> "Cancelled"

let private statusOf (state: string) =
    JobStateCodec.tryParse state
    |> Option.map JobStatus.forCustomer
    |> Option.defaultValue "Checking status…"

/// Urgency drives colour. A deadline that has passed must not look like one
/// comfortably ahead — that is the entire reason `Countdown` carries a rank
/// rather than just a string. Mapped onto the shared token set so a customer
/// flipping between this list and the tracking screen sees the same four
/// colours mean the same four things.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Theme.danger
    | Urgency.Urgent -> Theme.warning
    | Urgency.Soon -> Theme.calm
    | Urgency.Calm -> Theme.inkMuted

/// The one thing on this screen someone is here to do. Filled and full-width
/// so it reads as the primary action, not a fourth option among equals —
/// same Border-around-Button shape Chat's Send uses, so "the button you're
/// meant to press" looks the same everywhere in the app.
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

/// One active job. A hairline rather than the composer's heavier stroke —
/// this row repeats up to a handful of times a screen, and the thick edge is
/// reserved for the one control that must never look uncertain.
///
/// Built as its own function, not inlined into the `for` below: this is what
/// keeps a `Grid` (a layout) safely inside a `Border` (also a layout) inside
/// a loop — the CE inside `jobRow`'s body resolves on its own, so the outer
/// `VStack`'s `for` only ever yields one already-built widget.
let private jobRow (nav: Msg) (title: string) (subtitle: string) (subtitleColor: Microsoft.Maui.Graphics.Color) =
    Border(
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto; Auto ]) {
            Label(title)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(0).gridRow(0)
            Label(subtitle)
                .font(size = Theme.Font.subhead)
                .textColor(subtitleColor)
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
        .gestureRecognizers() { TapGestureRecognizer(nav) }

let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    let activeJobs = model.Jobs |> List.filter nonTerminal |> List.sortBy (fun j -> j.PromisedStart)
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        // Large title: this is the one top-level screen in the funnel, so it
        // gets the HIG large-title treatment everything downstream is inline
        // relative to.
        Label(sprintf "Hi, %s" name)
            .font(size = Theme.Font.largeTitle, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)

        primaryButton "Book a New Service" (Navigate Catalog)

        Label("Your active jobs")
            .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .padding(Thickness(0., Theme.Space.sm, 0., 0.))

        // Soonest first. The list opens on what is about to happen, which is
        // also the job whose countdown is worth watching.
        if List.isEmpty activeJobs then
            // Teaches the interface rather than announcing emptiness.
            Label("Nothing in flight")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
            Label("Book a service and it shows up here — from the moment someone accepts it to the moment they arrive.")
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
        else
            for j in activeJobs do
                let title = sprintf "%s — %s" j.ServiceName j.ProviderName
                match countdownFor model j with
                | Some c ->
                    jobRow (Navigate (Tracking j.Id))
                        title
                        (sprintf "%s · %s %s" (statusOf j.State) c.Label c.Value)
                        (urgencyColor c.Urgency)
                | None ->
                    jobRow (Navigate (Tracking j.Id)) title (statusOf j.State) Theme.inkMuted

        // Quiet, at the foot of the screen: an About page a real product has,
        // carrying the sample-photo credits.
        Button("About", Navigate About)
            .font(size = Theme.Font.footnote)
            .textColor(Theme.inkMuted)
            .horizontalOptions(Microsoft.Maui.Controls.LayoutOptions.Center)
            .padding(Thickness(0., Theme.Space.xl, 0., 0.))
     }).padding(Theme.screenMargin))
