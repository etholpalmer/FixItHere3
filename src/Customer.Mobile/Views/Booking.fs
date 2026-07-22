module FixItHere.Customer.Views.Booking

open Microsoft.Maui
open Microsoft.Maui.Controls
open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// One list, defined in Shared. The app used to keep its own, and the server
/// stored whatever string arrived — so a label the server could not resolve
/// would have booked a job at a time nobody chose.
let schedules = BookingSlot.options

/// "Now" books the job, but nobody says "now" when they mean "in twelve
/// minutes" — that mismatch is exactly the kind of small dishonesty this
/// build exists to remove. The wire value passed to `BookJob` is untouched;
/// only the label on glass changes.
let private displayLabel (s: string) =
    if s = "Now" then "As soon as possible" else s

/// Its own function, matching the other five screens in this funnel.
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

/// One time slot. Reads like choosing a time — a label plus the actual
/// instant it resolves to — rather than four identical grey buttons that
/// all say something different but look the same.
let private slotRow (msg: Msg) (title: string) (subtitle: string) =
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

let view (model: Model) (providerId: int) (serviceId: int) =
    // iPhone viewports are short; an unbounded VStack of rows had nowhere to
    // go on a small screen once a fifth slot or a longer label showed up.
    ScrollView(
     (VStack(spacing = Theme.Space.lg) {
        header "When should they come?"

        for s in schedules do
            let resolved =
                BookingSlot.tryResolve s model.DemoNow
                |> Option.map (fun t -> BookingSlot.describe t model.DemoNow)
                |> Option.defaultValue ""
            slotRow (BookJob (providerId, serviceId, s)) (displayLabel s) resolved
     }).padding(Theme.screenMargin))
