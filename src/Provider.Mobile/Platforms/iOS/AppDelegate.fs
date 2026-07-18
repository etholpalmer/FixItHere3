namespace Provider.Mobile

open Foundation
open Microsoft.Maui
open FixItHere.Provider

[<Register("AppDelegate")>]
type AppDelegate() =
    inherit MauiUIApplicationDelegate()

    override _.CreateMauiApp() = MauiProgram.createMauiApp ()
