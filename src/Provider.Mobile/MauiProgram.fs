module FixItHere.Provider.MauiProgram

open System.Threading.Tasks
open Fabulous
open Fabulous.Maui
open Microsoft.Maui.Devices
open Microsoft.Maui.Hosting
open Microsoft.Maui.Media
open Microsoft.Maui.Storage
open FixItHere.ClientShared
open FixItHere.Provider

/// Session persistence.
///
/// Plain `Preferences`, not `SecureStorage`: the token is "fake-customer-1"
/// (Auth.fs), and reaching for the keychain would dress a demo credential up as
/// a real one. Stored as four flat keys rather than serialised JSON so a shape
/// change cannot produce a half-parsed session — a missing key just means "not
/// signed in", which is the safe answer.
module private SessionStore =
    let private key n = "fixithere.session." + n

    let save (s: Session option) =
        match s with
        | Some v ->
            Preferences.Default.Set(key "token", v.Token)
            Preferences.Default.Set(key "userId", v.UserId)
            Preferences.Default.Set(key "role", v.Role)
            Preferences.Default.Set(key "name", v.DisplayName)
        | None ->
            for n in [ "token"; "userId"; "role"; "name" ] do
                Preferences.Default.Remove(key n)

    let restore () : Session option =
        let token = Preferences.Default.Get(key "token", "")
        let role = Preferences.Default.Get(key "role", "")
        let name = Preferences.Default.Get(key "name", "")
        let userId = Preferences.Default.Get(key "userId", 0)
        // Every field or nothing. A partially-written session would sign someone
        // in as an actor with no id, and this codebase has already spent four
        // separate bugs on identity being half-specified.
        if token <> "" && role <> "" && userId > 0
        then Some { Token = token; UserId = userId; Role = role; DisplayName = name }
        else None

/// Shrink a photo until it fits the wire budget, rather than refusing it.
///
/// The old code rejected anything over 100 KB with "Photo too large — pick a
/// smaller one", which is advice the user cannot act on: every photo a real
/// phone takes is several megabytes, so the feature was unusable by design.
/// Resizing is what every messaging app does, and it is the app's job, not the
/// user's.
///
/// iOS-only because the downscale goes through UIKit. Android keeps the
/// original bytes; it is not a demo target, and shipping a broken
/// platform-specific path would be worse than shipping none.
#if IOS
let private fitToBudget (bytes: byte[]) (budget: int) : byte[] =
    use data = Foundation.NSData.FromArray bytes
    match UIKit.UIImage.LoadFromData data with
    | null -> bytes
    | image ->
        // Step the longest edge down until the JPEG lands under budget. Quality
        // drops first because it is the cheaper axis visually; dimension only
        // after quality has stopped paying.
        let rec attempt (maxEdge: float32) (quality: float32) (tries: int) =
            let scale = min 1.0f (maxEdge / float32 (max image.Size.Width image.Size.Height))
            let size = CoreGraphics.CGSize(image.Size.Width * System.Runtime.InteropServices.NFloat(float scale), image.Size.Height * System.Runtime.InteropServices.NFloat(float scale))
            UIKit.UIGraphics.BeginImageContextWithOptions(size, false, System.Runtime.InteropServices.NFloat(1.0))
            image.Draw(CoreGraphics.CGRect(System.Runtime.InteropServices.NFloat(0.0), System.Runtime.InteropServices.NFloat(0.0), size.Width, size.Height))
            let scaled = UIKit.UIGraphics.GetImageFromCurrentImageContext()
            UIKit.UIGraphics.EndImageContext()
            use jpeg = scaled.AsJPEG(System.Runtime.InteropServices.NFloat(float quality))
            let out = jpeg.ToArray()
            if out.Length <= budget || tries = 0 then out
            elif quality > 0.45f then attempt maxEdge (quality - 0.15f) (tries - 1)
            else attempt (maxEdge * 0.7f) 0.7f (tries - 1)
        attempt 1280.0f 0.8f 6
#else
let private fitToBudget (bytes: byte[]) (_budget: int) = bytes
#endif

/// Base64 inflates by ~4/3, and the payload rides a JSON body, so the byte
/// budget is set below the wire limit rather than at it.
let private photoBudgetBytes = 90_000

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
                let fitted = if bytes.Length > photoBudgetBytes then fitToBudget bytes photoBudgetBytes else bytes
                if fitted.Length > photoBudgetBytes then
                    // Only reachable if the resize itself could not get there.
                    return Error "That photo could not be prepared for sending"
                else return Ok (System.Convert.ToBase64String fitted)
        with ex -> return Error ex.Message
    }
let private gpsLocation () : Task<Result<float * float, string>> =
    task {
        let! loc = Location.getCurrent (43.70, -79.45)
        return Ok loc
    }

/// Single HubClient instance shared two ways: its SendTyping/SendSeen methods are
/// injected into ProviderApiDeps below (Update.fs calls them on chat-draft/message-seen),
/// and its Start(...) method is invoked once on first LoggedIn further down. Config.baseUrl
/// must be finalized (Android emulator override) before this is constructed, since HubClient
/// captures the base URL at construction time.
let private hub =
    if DeviceInfo.Platform = DevicePlatform.Android then Config.baseUrl <- "http://10.0.2.2:5162"
    FixItHere.ClientShared.Hub.HubClient(Config.baseUrl)

let private deps =
    Api.createDepsWith pickPhoto gpsLocation hub.SendTyping hub.SendSeen SessionStore.save SessionStore.restore (new System.Net.Http.HttpClientHandler()) Config.baseUrl

let mutable private hubStarted = false
/// Guards the async connect window. `hubStarted` only latches once StartAsync
/// returns; without a second synchronous flag, every message arriving during
/// that window (JobsLoaded and ClockSynced both fire right after a restore)
/// would see `not hubStarted` and start the hub again.
let mutable private hubStarting = false

let private startHubCmd (role: string) (userId: int) =
    Cmd.ofSub (fun dispatch ->
        task {
            try
                do! hub.Start(
                        role, userId,
                        (HubJobUpdated >> dispatch), (HubMessageReceived >> dispatch),
                        (HubLocationUpdated >> dispatch), (HubNotification >> dispatch),
                        (fun (j, s, r) -> dispatch (HubTyping (j, s, r))),
                        (fun (j, s, r) -> dispatch (HubSeen (j, s, r))),
                        (HubProviderUpdated >> dispatch),
                        (ClockSynced >> dispatch))
                // Only latch once connected: WithAutomaticReconnect does not cover
                // the initial StartAsync, so latching before it succeeds would leave
                // the app permanently HTTP-only with no retry and no visible error.
                hubStarted <- true
            with ex ->
                // Clear the in-flight guard so the next session-bearing update retries.
                hubStarting <- false
                dispatch (ApiError (sprintf "Realtime unavailable: %s" ex.Message))
        } |> ignore)

/// Wraps Update.update: the first time the app holds a session it starts the
/// SignalR hub (the same HubClient whose SendTyping/SendSeen already feed `deps`).
///
/// Keyed on the *model's session*, not on the `LoggedIn` message: a returning
/// user is auto-restored to Home via `SplashDone` (task 0b), which never emits
/// `LoggedIn`. Matching the message left every restored session permanently
/// HTTP-only — no live status, chat, reschedule or arrival — which is exactly
/// the two-sided beat the demo turns on. Role/userId come from the session, so
/// login and restore share one path.
let private updateWithHub (msg: Msg) (model: Model) =
    let m, cmd = Update.update deps msg model
    match m.Session with
    | Some s when not hubStarted && not hubStarting ->
        hubStarting <- true
        m, Cmd.batch [ cmd; startHubCmd s.Role s.UserId ]
    | _ -> m, cmd

let program = Program.statefulWithCmd Update.init updateWithHub Views.Root.view

/// Note: a nested `type MauiProgram = static member CreateMauiApp()` (as in the
/// stock template) would collide with this file's own module name
/// (`FixItHere.Provider.MauiProgram`) — the nested type would only be reachable
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
