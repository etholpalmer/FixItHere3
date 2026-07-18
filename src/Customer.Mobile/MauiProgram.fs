module FixItHere.Customer.MauiProgram

open System.Threading.Tasks
open Fabulous
open Fabulous.Maui
open Microsoft.Maui.Devices
open Microsoft.Maui.Hosting
open Microsoft.Maui.Media
open FixItHere.Customer

let private pickPhoto () : Task<Result<string, string>> =
    task {
        try
            let! file = MediaPicker.Default.PickPhotoAsync()
            if isNull (box file) then return Error "No photo selected"
            else
                use! stream = file.OpenReadAsync()
                use ms = new System.IO.MemoryStream()
                do! stream.CopyToAsync(ms)
                let bytes = ms.ToArray()
                if bytes.Length > 100_000 then return Error "Photo too large — pick a smaller one"
                else return Ok (System.Convert.ToBase64String bytes)
        with ex -> return Error ex.Message
    }

let private gpsLocation () : Task<Result<float * float, string>> =
    task {
        let! loc = Location.getCurrent (43.65, -79.38)
        return Ok loc
    }

let private deps =
    if DeviceInfo.Platform = DevicePlatform.Android then Config.baseUrl <- "http://10.0.2.2:5000"
    Api.createDepsWith pickPhoto gpsLocation (new System.Net.Http.HttpClientHandler()) Config.baseUrl

let mutable private hubStarted = false

/// Wraps Update.update: first successful login also starts the SignalR hub.
let private updateWithHub (msg: Msg) (model: Model) =
    let m, cmd = Update.update deps msg model
    match msg with
    | LoggedIn _ when not hubStarted ->
        hubStarted <- true
        let hubCmd =
            Cmd.ofSub (fun dispatch ->
                Hub.HubClient(Config.baseUrl).Start(dispatch) |> ignore)
        m, Cmd.batch [ cmd; hubCmd ]
    | _ -> m, cmd

let program = Program.statefulWithCmd Update.init updateWithHub Views.Root.view

/// Note: a nested `type MauiProgram = static member CreateMauiApp()` (as in the
/// brief and the stock template) would collide with this file's own module name
/// (`FixItHere.Customer.MauiProgram`) — the nested type would only be reachable
/// as `MauiProgram.MauiProgram.CreateMauiApp()`. Using a plain function instead
/// keeps the call sites in the Platforms/* files as `MauiProgram.createMauiApp()`.
let createMauiApp () =
    MauiApp.CreateBuilder()
        .UseFabulousApp(program)
        .ConfigureFonts(fun fonts ->
            fonts
                .AddFont("OpenSans-Regular.ttf", "OpenSansRegular")
                .AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold")
            |> ignore)
        .Build()
