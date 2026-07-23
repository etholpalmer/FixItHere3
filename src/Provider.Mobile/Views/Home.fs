module FixItHere.Provider.Views.Home

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared

/// Urgency drives colour. A departure time already passed must not look like
/// one comfortably ahead. Mapped onto the shared token set so a provider
/// bouncing between this list and the active job screen sees the same four
/// colours mean the same four things.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Theme.danger
    | Urgency.Urgent -> Theme.warning
    | Urgency.Soon -> Theme.calm
    | Urgency.Calm -> Theme.inkMuted

/// The shift switch. A native `Switch` rather than a button that relabels
/// itself — this is exactly the platform control HIG reserves for a binary
/// state, and the dot carries "online or not" before the label is even read.
/// Its own function so the Grid inside the Border resolves independently of
/// the outer VStack (FS0792).
let private onlineRow (online: bool) =
    Border(
        Grid(coldefs = [ Auto; Star; Auto ], rowdefs = [ Auto ]) {
            Label(if online then "●" else "○")
                .font(size = Theme.Font.headline)
                .textColor(if online then Theme.success else Theme.inkMuted)
                .gridColumn(0)
                .centerVertical()
            Label(if online then "Online — visible for new jobs" else "Offline")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(1)
                .centerVertical()
                .padding(Thickness(Theme.Space.sm, 0., 0., 0.))
            Switch(online, SetOnline)
                .gridColumn(2)
                .centerVertical()
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.md, Theme.Space.md))

/// What the provider is told about their one active job, without a raw
/// enum leaking through if a future state slips past this app's copy.
let private stateLine (j: FixItHere.Shared.Dtos.JobDto) =
    JobStateCodec.tryParse j.State
    |> Option.map JobStatus.forProvider
    |> Option.defaultValue "Checking status…"

/// The one job currently being worked, surfaced above the available list so
/// it never competes with jobs not yet accepted. Brand-washed rather than
/// the neutral treatment every available row gets below — this is what the
/// provider is doing right now, not one more thing they could choose to do.
/// Its own function so the Grid inside the Border resolves independently of
/// the outer VStack.
let private activeJobCard (model: Model) (j: FixItHere.Shared.Dtos.JobDto) =
    Border(
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto; Auto; Auto; Auto ]) {
            Label("ACTIVE JOB")
                .font(size = Theme.Font.caption, attributes = FontAttributes.Bold)
                .textColor(Theme.brandInk)
                .gridColumn(0).gridRow(0)
            Label(sprintf "%s — %s" j.ServiceName j.CustomerName)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.brandInk)
                .gridColumn(0).gridRow(1)
            Label(stateLine j)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.brandInk)
                .gridColumn(0).gridRow(2)
            // Split for the same reason as `availableJobRow` below: the label
            // is prose and must wrap, the clock is not and must not.
            match countdownFor model j with
            | Some c ->
                Label(c.Label)
                    .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
                    .lineBreakMode(LineBreakMode.WordWrap)
                    .gridColumn(0).gridRow(3)
                Label(c.Value)
                    .font(size = Theme.Font.title2, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
                    .gridColumn(1).gridRowSpan(4)
                    .centerVertical()
                    .padding(Thickness(Theme.Space.md, 0., 0., 0.))
            | None ->
                Label("›")
                    .font(size = Theme.Font.title1)
                    .textColor(Theme.brandInk)
                    .gridColumn(1).gridRowSpan(4)
                    .centerVertical()
        })
        .stroke(Theme.brandEdge)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.brandWash)
        .padding(Theme.Space.lg)
        .gestureRecognizers() { TapGestureRecognizer(Navigate (ActiveJob j.Id)) }

/// One available job, soonest-to-leave first, with the number that decides
/// whether to take it. Address is deliberately absent here — on a phone it
/// ran off the right edge as "…The Be", and a truncated address is worse
/// than none; it lives one tap away on the detail screen. Built as its own
/// function, not inlined into the `for` below: this is what keeps a Grid (a
/// layout) safely inside a Border (also a layout) inside a loop — Fabulous
/// CE rejects a nested layout container yielded directly inside a `for`
/// (FS0792).
let private availableJobRow (model: Model) (j: FixItHere.Shared.Dtos.JobDto) =
    Border(
        Grid(coldefs = [ Star; Auto ], rowdefs = [ Auto; Auto; Auto ]) {
            Label(sprintf "%s — %s" j.ServiceName j.CustomerName)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .gridColumn(0).gridRow(0)
            Label(Format.money j.Price)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
                .gridColumn(0).gridRow(1)
            // Label and value are split across the two columns rather than
            // set as one string. Countdown labels range from "Leave in" to
            // "Late — reportable as a no-show in", and the long one in an
            // `Auto` column starved the `Star` column of every pixel — the
            // row rendered as a single clipped line of red with both ends
            // cut off. The wrapping caption lives on the left where there is
            // room to wrap; only the clock, which is never wide, sits right.
            match countdownFor model j with
            | Some c ->
                Label(c.Label)
                    .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
                    .lineBreakMode(LineBreakMode.WordWrap)
                    .gridColumn(0).gridRow(2)
                Label(c.Value)
                    .font(size = Theme.Font.title2, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
                    .gridColumn(1).gridRowSpan(3)
                    .centerVertical()
                    .padding(Thickness(Theme.Space.md, 0., 0., 0.))
            | None ->
                Label("›")
                    .font(size = Theme.Font.title2)
                    .textColor(Theme.inkMuted)
                    .gridColumn(1).gridRowSpan(3)
                    .centerVertical()
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Thickness(Theme.Space.lg, Theme.Space.md, Theme.Space.md, Theme.Space.md))
        .gestureRecognizers() { TapGestureRecognizer(Navigate (JobDetail j.Id)) }

let view (model: Model) =
    let name = model.Session |> Option.map (fun s -> s.DisplayName) |> Option.defaultValue ""
    ScrollView(
     (VStack(spacing = Theme.Space.md) {
        // Large title: the one top-level screen a provider lands on, so it
        // gets the HIG large-title treatment everything below is inline
        // relative to. No greeting flourish — this app earns its keep on
        // speed, not on reassurance.
        Label(name)
            .font(size = Theme.Font.largeTitle, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)

        onlineRow model.Online

        match activeJob model with
        | Some j ->
            Label("Active job")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))
            activeJobCard model j
        | None -> ()

        if model.Online then
            Label("Available jobs")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))

            let available =
                model.Jobs
                |> List.filter (fun j -> j.State = "Scheduled")
                |> List.sortBy (fun j -> j.PromisedStart)

            if List.isEmpty available then
                // Teaches the interface rather than announcing emptiness.
                Label("No jobs waiting right now")
                    .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                    .textColor(Theme.ink)
                Label("New requests show up here the moment someone books — you'll see the payout and how long you have to leave.")
                    .font(size = Theme.Font.subhead)
                    .textColor(Theme.inkMuted)
            else
                for j in available do
                    availableJobRow model j
        else
            Label("You're offline")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
            Label("Go online to start seeing new job requests come in.")
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
     }).padding(Theme.gutter))
