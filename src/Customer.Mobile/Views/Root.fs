module FixItHere.Customer.Views.Root

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared

/// Colour carries the kind. Every notification used to render in the same grey
/// bar, so "Your provider never arrived" and "Provider Accepted" looked alike.
let private noticeColor (k: NoticeKind) =
    match k with
    | NoticeKind.Success -> Microsoft.Maui.Graphics.Color.FromRgb(0x1B, 0x5E, 0x3A)
    | NoticeKind.Warning -> Microsoft.Maui.Graphics.Color.FromRgb(0x8A, 0x4B, 0x08)
    | NoticeKind.Ask -> Microsoft.Maui.Graphics.Color.FromRgb(0x1E, 0x3A, 0x8A)
    | NoticeKind.Info -> Microsoft.Maui.Graphics.Color.FromRgb(0x33, 0x33, 0x3D)

let private screenView (model: Model) =
    match model.Screen with
    | Splash -> AnyView(Splash.view model)
    | Login -> AnyView(Login.view model)
    | Home -> AnyView(Home.view model)
    | Catalog -> AnyView(Catalog.view model)
    | ProviderList _ -> AnyView(ProviderList.view model)
    | ProviderProfile id -> AnyView(ProviderProfile.view model id)
    | Booking (pid, sid) -> AnyView(Booking.view model pid sid)
    | Tracking id -> AnyView(Tracking.view model id)
    | Chat id -> AnyView(Chat.view model id)
    | Payment id -> AnyView(Payment.view model id)
    | Rating id -> AnyView(Rating.view model id)
    | About -> AnyView(About.view model)

let view (model: Model) =
    Application(
        ContentPage(
            (Grid(coldefs = [ Star ], rowdefs = [ Star ]) {
                screenView model
                // The stack, newest first. A single slot silently replaced
                // whatever was there — so the two-sided beats this phase exists
                // to show could overwrite each other mid-demo.
                // Floating toast cards, not a full-bleed bar: inset from the
                // edges with a top margin that clears the status bar, and
                // rounded, so a notice reads as a notification laid over the
                // screen rather than as something that has replaced its header.
                (VStack(spacing = Theme.gapTight) {
                    for n in model.Notices do
                        Border(
                            Label(n.Text)
                                .textColor(Microsoft.Maui.Graphics.Colors.White)
                                .padding(Theme.gap))
                            .background(noticeColor n.Kind)
                            .stroke(noticeColor n.Kind)
                            .strokeShape(RoundRectangle(cornerRadius = Theme.radiusControl))
                            .gestureRecognizers() { TapGestureRecognizer(DismissNotice n.Id) }
                })
                    .verticalOptions(Microsoft.Maui.Controls.LayoutOptions.Start)
                    .margin(Microsoft.Maui.Thickness(Theme.gap, Theme.Space.xxl, Theme.gap, 0.))
                match model.Error with
                | Some e ->
                    Label(sprintf "⚠ %s" e)
                        .background(Microsoft.Maui.Graphics.Colors.DarkRed)
                        .textColor(Microsoft.Maui.Graphics.Colors.White)
                        .padding(12.)
                        .verticalOptions(Microsoft.Maui.Controls.LayoutOptions.End)
                        .gestureRecognizers() { TapGestureRecognizer(DismissError) }
                | None -> ()
                if model.FakeCallActive then
                    (VStack(spacing = 16.) {
                        Label("Calling provider…").font(size = 28.).textColor(Microsoft.Maui.Graphics.Colors.White).centerTextHorizontal()
                        ActivityIndicator(true)
                        Button("End Call", EndFakeCall)
                    }).background(Microsoft.Maui.Graphics.Color.FromRgba(0., 0., 0., 0.85)).centerVertical()
            })
        )
    )
