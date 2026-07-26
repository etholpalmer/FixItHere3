module FixItHere.Provider.Views.ActiveJob

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared


/// Memoised map *source object*, not just its HTML.
///
/// Caching the string was necessary and not sufficient: the view wrapped it in
/// `HtmlWebViewSource(Html = ...)`, constructing a brand-new object on every
/// render. With the 250 ms countdown pump that is four new sources a second,
/// and Fabulous re-sets a property whose value is a different reference — so
/// the WebView reloaded continuously and the map visibly flashed.
///
/// A reload also drops and re-opens the page's own SignalR connection, which is
/// why "no hub churn" was the wrong thing to measure: the page came back faster
/// than a dropped connection took to surface.
///
/// Handing back the same instance makes the diff a no-op. Keyed on what the map
/// actually depends on, so moving to a different job still rebuilds it.
module private MapCache =
    let private cache =
        System.Collections.Generic.Dictionary<struct (float * float * int * string), HtmlWebViewSource>()

    let source (baseUrl: string) (lat: float) (lng: float) (providerId: int) (destLabel: string) =
        let key = struct (lat, lng, providerId, destLabel)
        match cache.TryGetValue key with
        | true, v -> v
        | _ ->
            let v = HtmlWebViewSource(Html = MapHtml.render baseUrl lat lng providerId destLabel)
            cache[key] <- v
            v

/// Urgency drives colour, mapped onto the shared token set so this screen
/// agrees with Home and JobDetail about what each colour means — a provider
/// bouncing between screens must never see the same word paired with two
/// different colours.
let private urgencyColor (u: Urgency) =
    match u with
    | Urgency.Overdue -> Theme.danger
    | Urgency.Urgent -> Theme.warning
    | Urgency.Soon -> Theme.calm
    | Urgency.Calm -> Theme.inkMuted

let private statusLine (state: string) (cancelledBy: string) =
    match JobStateCodec.tryParse state with
    | Some Cancelled -> JobStatus.cancelledBy ActorRole.Provider (ActorRole.ofWire cancelledBy)
    | Some s -> JobStatus.forProvider s
    | None -> "Checking status…"

/// The single state-driven next action for this job (spec: one button, driven
/// by job state). Shared decides *which* transition; this only maps it to the
/// app's own Msg, so the button label and the event it fires cannot disagree.
///
/// Returns the label, the direct Msg, and whether the action needs a confirming
/// second tap. Arrive / Start Work / Complete each advance the job irreversibly
/// from a button under the operator's thumb on the map, so they confirm; Depart
/// (the deliberate "I'm heading out") stays one tap.
let private actionButton (j: FixItHere.Shared.Dtos.JobDto) =
    JobStateCodec.tryParse j.State
    |> Option.bind JobStatus.nextProviderAction
    |> Option.map (fun (label, ev) ->
        let msg =
            match ev with
            | DepartEnRoute -> Depart j.Id
            | Arrive -> MarkArrived j.Id
            | StartWork -> BeginWork j.Id
            | CompleteWork -> FinishWork j.Id
            | Accepted | RateAndClose | Cancel | MarkNoShow -> Depart j.Id
        let needsConfirm =
            match ev with
            | Arrive | StartWork | CompleteWork -> true
            | _ -> false
        label, msg, needsConfirm)

/// Denser provider mirror of the customer Tracking screen's `statusCard`:
/// who, what, and the two numbers this whole screen exists to show — the
/// countdown and the payout. Its own function so the Border and the match on
/// the countdown inside it resolve independently of the outer Grid.
let private statusCard (model: Model) (job: FixItHere.Shared.Dtos.JobDto) =
    Border(
        VStack(spacing = Theme.Space.xs) {
            Button("‹", GoBack)
                .font(size = Theme.Font.title1)
                .textColor(Theme.brand)
                .width(Theme.touchTarget).height(Theme.touchTarget)

            Label(statusLine job.State job.CancelledBy)
                .font(size = Theme.Font.title3, attributes = FontAttributes.Bold)
                .textColor(Theme.ink)

            // The countdown is the headline, not a footnote: it is the
            // number this screen exists to show. Two stacked Labels rather
            // than one interpolated string — "Late — reportable as a
            // no-show in 43:46" set at title scale is wider than a phone,
            // and the caption is what got clipped. Splitting them also puts
            // the scale contrast where it belongs: prose small, clock large.
            // Two Labels rather than a nested HStack — Fabulous CE rejects
            // nesting here.
            match countdownFor model job with
            | Some c ->
                Label(c.Label)
                    .font(size = Theme.Font.subhead, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
                    .lineBreakMode(Microsoft.Maui.LineBreakMode.WordWrap)
                Label(c.Value)
                    .font(size = Theme.Font.title1, attributes = FontAttributes.Bold)
                    .textColor(urgencyColor c.Urgency)
            | None -> ()

            Label(sprintf "%s — %s" job.CustomerName job.Address)
                .font(size = Theme.Font.subhead)
                .textColor(Theme.ink)
                .lineBreakMode(Microsoft.Maui.LineBreakMode.WordWrap)

            Label(Format.money job.Price)
                .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
                .textColor(Theme.brandInk)
        })
        .stroke(Theme.surfaceEdge)
        .strokeThickness(Theme.strokeHair)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.surface)
        .padding(Theme.Space.lg)

/// Running-late / pending-proposal bar. Offered only while an arrival is
/// still ahead of us, and only when nothing is already pending — the server
/// refuses a second proposal, so a button that could raise one would be a
/// lie. One flat HStack: Fabulous CE rejects nested layouts here.
let private lateControlsBar (lateControlsVisible: bool) (proposalPending: bool) (jobId: int) =
    HStack(spacing = Theme.Space.sm) {
        if lateControlsVisible then
            Label("Running late?")
                .font(size = Theme.Font.footnote)
                .textColor(Theme.inkMuted)
                .centerVertical()
            for mins in [ 10; 15; 30 ] do
                Button(sprintf "+%dm" mins, ProposeDelay (jobId, mins))
                    .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                    .textColor(Theme.brandInk)
                    .background(Theme.brandWash)
        elif proposalPending then
            Label("Waiting for the customer to answer")
                .font(size = Theme.Font.footnote, attributes = FontAttributes.Bold)
                .textColor(Theme.warning)
                .centerVertical()
    }

/// The one clear next action, and the app's spine: full-width, unmistakable,
/// and the only filled control on the screen. Success-green rather than the
/// brand-honey `primaryButton` shape JobDetail's Accept uses — this button
/// *advances* a job already under way, and the colour tells a provider
/// bouncing between screens which kind of primary action they are looking
/// at. Chat/Call/Cancel below stay plain text, visibly secondary.
let private nextActionButton (label: string) (msg: Msg) =
    Border(
        Button(label, msg)
            .font(size = Theme.Font.headline, attributes = FontAttributes.Bold)
            .textColor(Theme.onBrand))
        .stroke(Theme.success)
        .strokeThickness(Theme.strokeThick)
        .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
        .background(Theme.success)
        .padding(Thickness(Theme.Space.xs, Theme.Space.xs, Theme.Space.xs, Theme.Space.xs))

/// The next-action row. Always a VStack (one type, so it drops into the Grid
/// cell cleanly and the CE handles the conditional children — the same shape as
/// `lateControlsBar`). For Arrive / Start Work / Complete the first tap arms a
/// confirm: the button becomes "Yes — <label>" with a plain "Not yet" beneath,
/// so a stray tap on the map can't skip an irreversible step. Depart is one tap.
let private actionRow (model: Model) (j: FixItHere.Shared.Dtos.JobDto) =
    VStack(spacing = Theme.Space.xs) {
        match actionButton j with
        | Some (label, msg, needsConfirm) ->
            if needsConfirm && model.ConfirmingAction = Some j.Id then
                nextActionButton (sprintf "Yes — %s" label) (ConfirmAction j.Id)
                Button("Not yet", DismissAction)
                    .font(size = Theme.Font.callout)
                    .textColor(Theme.inkMuted)
                    .horizontalOptions(Microsoft.Maui.Controls.LayoutOptions.Center)
            elif needsConfirm then
                nextActionButton label (RequestAction j.Id)
            else
                nextActionButton label msg
        | None -> ()
    }

/// Chat, Call, Cancel — visibly secondary to the action button above: plain
/// text, no fill, so the eye lands on the one button that actually advances
/// the job. Asks before acting: cancelling is irreversible and was one tap
/// away, on a screen an investor is handed to poke at.
let private secondaryBar (model: Model) (job: FixItHere.Shared.Dtos.JobDto) =
    HStack(spacing = Theme.Space.xl) {
        Button("Call", StartFakeCall)
            .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
            .textColor(Theme.brand)
        Button("Chat", Navigate (Chat job.Id))
            .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
            .textColor(Theme.brand)
        if model.ConfirmingCancel = Some job.Id then
            Button("Yes, cancel", CancelJob job.Id)
                .font(size = Theme.Font.callout, attributes = FontAttributes.Bold)
                .textColor(Theme.danger)
            Button("Keep it", DismissCancel)
                .font(size = Theme.Font.callout)
                .textColor(Theme.inkMuted)
        else
            Button("Cancel Job", RequestCancel job.Id)
                .font(size = Theme.Font.callout)
                .textColor(Theme.inkMuted)
    }

let view (model: Model) (jobId: int) =
    match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
    | None ->
        AnyView(
            (VStack(spacing = Theme.Space.md) {
                Button("‹", GoBack).font(size = Theme.Font.title1).textColor(Theme.brand).width(Theme.touchTarget).height(Theme.touchTarget)
                Label("Job not found").font(size = Theme.Font.body).textColor(Theme.inkMuted)
            }).padding(Theme.gutter))
    | Some job ->
        let sched : Reschedule = rescheduleOf job
        let awaitingArrival =
            JobStateCodec.tryParse job.State
            |> Option.map JobStatus.awaitsArrival
            |> Option.defaultValue false
        let proposalPending = sched.Pending.IsSome
        let lateControlsVisible = awaitingArrival && not proposalPending
        AnyView(
            (Grid(coldefs = [ Star ], rowdefs = [ Auto; Star; Auto; Auto; Auto ]) {
                (statusCard model job).gridRow(0)

                // The customer's name, not "You" — this pin is their doorstep.
                WebView(MapCache.source Config.baseUrl job.Lat job.Lng job.ProviderId job.CustomerName).gridRow(1)

                (lateControlsBar lateControlsVisible proposalPending job.Id)
                    .padding(Thickness(0., Theme.Space.xs, 0., Theme.Space.xs))
                    .gridRow(2)

                // The one clear next action. Its own row so it can be
                // full-width and unmissable — never sharing a row with
                // Chat/Call/Cancel the way a wall-of-buttons layout would.
                // Progress steps (Arrive/Start/Complete) arm a confirm here.
                (actionRow model job).gridRow(3)

                (secondaryBar model job)
                    .padding(Thickness(0., Theme.Space.sm, 0., 0.))
                    .gridRow(4)
            }).padding(Theme.Space.md)
        )
