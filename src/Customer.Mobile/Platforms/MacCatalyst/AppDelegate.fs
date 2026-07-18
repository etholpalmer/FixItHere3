namespace Customer.Mobile

open Foundation
open Microsoft.Maui
open FixItHere.Customer

[<Register("AppDelegate")>]
type AppDelegate() =
    inherit MauiUIApplicationDelegate()

    override this.CreateMauiApp() = MauiProgram.createMauiApp ()
