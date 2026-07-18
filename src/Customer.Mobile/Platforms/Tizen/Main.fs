namespace Customer.Mobile

open System
open Microsoft.Maui
open Microsoft.Maui.Hosting
open FixItHere.Customer

type Program() =
    inherit MauiApplication()

    override this.CreateMauiApp() = MauiProgram.createMauiApp ()

module Program =
    [<EntryPoint>]
    let main args =
        let app = Program()
        app.Run(args)
