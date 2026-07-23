module FixItHere.Provider.Views.JobDetail

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared

/// Urgency drives colour, mapped onto the shared token set so this screen
/// agrees with Home and the active job screen about what each colour means.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Theme.danger
    | Urgency.Urgent -> Theme.warning
    | Urgency.Soon -> Theme.calm
    | Urgency.Calm -> Theme.inkMuted

/// Inline header: this is a step down from Home, not a tab root, so it takes
/// the back-chevron treatment rather than a second large title. Its own
/// function for the same reason Catalog's `header` is in the customer app —
/// a Grid yielded directly inside the outer VStack's CE trips FS0792
/// otherwise.
let private header (title: string) =
    Grid(coldefs = [ Auto; Star ], rowdefs = [ Auto ]) {
        Button("‹", GoBack)
            .font(size = Theme.Font.title1)
            .textColor(Theme.brand)
            .width(Theme.touchTarget).height(Theme.touchTarget)
            .gridColumn(0)
        Label(title)
            .font(size = Theme.Font.title2, attributes = FontAttributes.Bold)
            .textColor(Theme.ink)
            .gridColumn(1)
            .centerVertical()
    }

/// What this job pays, where it is, and when it needs to be left for — the
/// three facts a provider decides Accept from. Its own function so the
/// Border resolves independently of the outer VStack.
let private jobCard (model: Model) (j: FixItHere.Shared.Dtos.JobDto) =
    Border(
        VStack(spacing = Theme.Space.xs) {
            Label(j.CustomerName)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)
            Label(j.Address)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.inkMuted)
                .lineBreakMode(Microsoft.Maui.LineBreakMode.WordWrap)
            // Format.money, not "$%M". A price rendered as "$277.5" is a
            // number that escaped, not a price — and this is the number a
            // provider is actually deciding from, so it gets the biggest
            // type on the card.
            Label(Format.money j.Price)
                .font(size = Theme.Font.title1, attributes = FontAttributes.Bold)
                .textColor(Theme.brandInk)
            // BookingSlot.describe, not the raw field — this screen once
            // showed an ISO timestamp straight off the wire.
            Label(sprintf "Arrive %s" (BookingSlot.describe (rescheduleOf j).PromisedStart model.DemoNow))
                .font(size = Theme.Font.subhead)
                .textColor(Theme.ink)
            match countdownFor model j with
            | Some c ->
                Label(Countdown.oneLine c)
                    .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
            | None -> ()
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Theme.Space.lg)

/// The one thing this screen exists for. Filled and full-width so it reads
/// as the primary action, not a fourth option among equals — same
/// Border-around-Button shape the customer app's primary CTA uses, so "the
/// button you're meant to press" looks the same across both apps.
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
    let job = model.Jobs |> List.tryFind (fun j -> j.Id = jobId)
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        match job with
        | Some j ->
            header j.ServiceName
            jobCard model j
            primaryButton "Accept Job" (AcceptJob j.Id)
        | None ->
            header "Job request"
            Label("This job is no longer available.")
                .font(size = Theme.Font.body)
                .textColor(Theme.inkMuted)
     }).padding(Theme.gutter))
