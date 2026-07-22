module FixItHere.ClientShared.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR.Client
open FixItHere.Shared.Dtos

type HubClient(baseUrl: string) =
    let conn =
        HubConnectionBuilder().WithUrl(baseUrl + "/hub").WithAutomaticReconnect().Build()

    /// Fire-and-forget by design (a dropped typing/seen ping is not worth
    /// surfacing), but faults are observed rather than left to the finalizer
    /// thread as unobserved task exceptions.
    let fireAndForget (t: Task) =
        t.ContinueWith(
            (fun (completed: Task) ->
                if completed.IsFaulted then
                    System.Diagnostics.Debug.WriteLine(
                        sprintf "hub send failed: %s" (string completed.Exception))),
            TaskContinuationOptions.ExecuteSynchronously)
        |> ignore

    /// `role`/`userId` identify this actor to the server so it can be put in a
    /// group and receive only its own job traffic. Rejoining after a reconnect is
    /// handled here rather than by callers: SignalR does not preserve group
    /// membership across a reconnect, and a caller that forgets produces an app
    /// that looks connected while silently receiving nothing.
    member _.Start
        (role: string, userId: int,
         onJob: JobDto -> unit, onMessage: MessageDto -> unit,
         onLocation: LocationDto -> unit, onNotification: string -> unit,
         onTyping: int * int * string -> unit, onSeen: int * int * string -> unit,
         onProvider: ProviderDto -> unit,
         onClock: DemoClockDto -> unit) : Task =
        conn.On<JobDto>("JobUpdated", onJob) |> ignore
        conn.On<MessageDto>("MessageReceived", onMessage) |> ignore
        conn.On<LocationDto>("LocationUpdated", onLocation) |> ignore
        conn.On<string>("Notification", onNotification) |> ignore
        conn.On<ProviderDto>("ProviderUpdated", onProvider) |> ignore
        // Pushed, not polled. Pausing the clock has to reach both phones
        // immediately, or the operator pauses to talk and the countdowns keep
        // running on screen behind them.
        conn.On<DemoClockDto>("ClockUpdated", onClock) |> ignore
        conn.On<int, int, string>("Typing", fun j s r -> onTyping (j, s, r)) |> ignore
        conn.On<int, int, string>("Seen", fun j s r -> onSeen (j, s, r)) |> ignore
        // F# cannot use .Add here — SignalR's Reconnected is a Func<string,Task>,
        // not an F# event (FS1091). Use the explicit add_ accessor.
        conn.add_Reconnected(fun _ ->
            conn.InvokeAsync("JoinActor", role, userId))
        task {
            do! conn.StartAsync()
            do! conn.InvokeAsync("JoinActor", role, userId)
        } :> Task

    member _.SendTyping (jobId: int) (senderId: int) (senderRole: string) : unit =
        if conn.State = HubConnectionState.Connected then
            fireAndForget (conn.InvokeAsync("SendTyping", jobId, senderId, senderRole))
    member _.SendSeen (jobId: int) (senderId: int) (senderRole: string) : unit =
        if conn.State = HubConnectionState.Connected then
            fireAndForget (conn.InvokeAsync("SendSeen", jobId, senderId, senderRole))
