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

/// The shift row. A native `Switch` rather than a button that relabels itself
/// — this is exactly the platform control HIG reserves for a binary state, and
/// the dot carries the answer before the label is even read.
///
/// The switch is *absent*, not disabled, while a job is under way. Availability
/// is not the provider's to set at that moment: they are at a customer's
/// address, and the only thing that puts them back on the market is finishing.
/// A greyed-out control would invite a tap and explain nothing; the sentence
/// beside the dot does the explaining instead.
///
/// Its own function so the Grid inside the Border resolves independently of
/// the outer VStack (FS0792).
let private shiftRow (state: Availability) =
    let dot, dotColor, text =
        match state with
        | Availability.Available -> "●", Theme.success, "Online — visible for new jobs"
        | Availability.OnAJob -> "●", Theme.brand, "On a job — new requests paused"
        | Availability.Offline -> "○", Theme.inkMuted, "Offline"
    Border(
        Grid(coldefs = [ Auto; Star; Auto ], rowdefs = [ Auto ]) {
            Label(dot)
                .font(size = Theme.Font.headline)
                .textColor(dotColor)
                .gridColumn(0)
                .centerVertical()
            Label(text)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .lineBreakMode(LineBreakMode.WordWrap)
                .gridColumn(1)
                .centerVertical()
                .padding(Thickness(Theme.Space.sm, 0., 0., 0.))
            match state with
            | Availability.OnAJob -> ()
            | _ ->
                // Parenthesised: bare `state = ...` reads as a named argument.
                Switch((state = Availability.Available), SetOnline)
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

        shiftRow (availability model)

        match activeJob model with
        | Some j ->
            Label("Active job")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))
            activeJobCard model j
        | None -> ()

        match availability model with
        | Availability.Available ->
            Label("Available jobs")
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))

            // Scheduled AND not yet mine. An accepted job is still Scheduled,
            // but it has left the market — it shows above as the active job,
            // ready to depart — so it must not reappear here looking untaken.
            let available =
                model.Jobs
                |> List.filter (fun j -> j.State = "Scheduled" && not j.IsAccepted)
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

        // The list is withheld rather than shown-and-refused. Offering rows a
        // provider cannot act on would be the screen dangling work at someone
        // already standing in a customer's kitchen, and it says plainly what
        // brings them back rather than leaving them to find the toggle.
        | Availability.OnAJob ->
            Label("New requests are paused")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
                .padding(Thickness(0., Theme.Space.sm, 0., 0.))
            Label("You're on a job. Finish it and the next requests come straight back.")
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)

        | Availability.Offline ->
            Label("You're offline")
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
            Label("Go online to start seeing new job requests come in.")
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
     }).padding(Theme.gutter))
