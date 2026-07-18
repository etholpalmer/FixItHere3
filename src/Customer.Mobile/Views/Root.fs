module FixItHere.Customer.Views.Root

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Customer

let private placeholder (name: string) =
    (VStack(spacing = 12.) { Button("← Back", GoBack); Label(name) }).padding(24.)

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
    | Payment _ -> AnyView(placeholder "Payment (Task 9)")
    | Rating _ -> AnyView(placeholder "Rating (Task 9)")
    | DevSettings -> AnyView(placeholder "DevSettings (Task 9)")

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
            })
        )
    )
