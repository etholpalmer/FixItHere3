namespace Customer.Mobile

open Android.App
open Microsoft.Maui
open FixItHere.Customer

[<Application>]
type MainApplication(handle, ownership) =
    inherit MauiApplication(handle, ownership)

    override _.CreateMauiApp() = MauiProgram.createMauiApp ()
