module FixItHere.Provider.Views.Root

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

/// Stand-in for screens not yet built (Payment/RateCustomer/DevSettings —
/// arriving in Task 10). Lets Navigate/back-stack wiring work end-to-end today.
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
    | ActiveJob id -> ActiveJob.view model id
    | Chat id -> AnyView(Chat.view model id)
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
