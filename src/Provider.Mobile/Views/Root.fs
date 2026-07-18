module FixItHere.Provider.Views.Root

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

/// Stand-in for screens not yet built (ActiveJob/Chat/Payment/RateCustomer/DevSettings —
/// arriving in Tasks 9-10). Lets Navigate/back-stack wiring work end-to-end today.
let private placeholder (label: string) =
    (VStack(spacing = 12.) {
        Label(label).font(size = 20.)
        Button("← Back", GoBack)
    }).padding(24.)

let private screenView (model: Model) =
    match model.Screen with
    | Splash -> AnyView(Splash.view model)
    | Login -> AnyView(Login.view model)
    | Home -> AnyView(Home.view model)
    | JobDetail id -> AnyView(JobDetail.view model id)
    | ActiveJob id -> AnyView(placeholder (sprintf "Active Job #%d (coming soon)" id))
    | Chat id -> AnyView(placeholder (sprintf "Chat #%d (coming soon)" id))
    | Payment id -> AnyView(placeholder (sprintf "Payment #%d (coming soon)" id))
    | RateCustomer id -> AnyView(placeholder (sprintf "Rate Customer #%d (coming soon)" id))
    | DevSettings -> AnyView(placeholder "Developer Settings (coming soon)")

let view (model: Model) =
    Application(
        ContentPage(
            (Grid(coldefs = [ Star ], rowdefs = [ Star ]) {
                screenView model
                match model.Toast with
                | Some t ->
                    Label(t)
                        .background(Microsoft.Maui.Graphics.Colors.DarkSlateBlue)
                        .textColor(Microsoft.Maui.Graphics.Colors.White)
                        .padding(12.)
                        .verticalOptions(Microsoft.Maui.Controls.LayoutOptions.Start)
                        .gestureRecognizers() { TapGestureRecognizer(DismissToast) }
                | None -> ()
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
                        Label("Calling customer…").font(size = 28.).textColor(Microsoft.Maui.Graphics.Colors.White).centerTextHorizontal()
                        ActivityIndicator(true)
                        Button("End Call", EndFakeCall)
                    }).background(Microsoft.Maui.Graphics.Color.FromRgba(0., 0., 0., 0.85)).centerVertical()
            })
        )
    )
