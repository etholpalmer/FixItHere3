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

    member _.Start
        (onJob: JobDto -> unit, onMessage: MessageDto -> unit,
         onLocation: LocationDto -> unit, onNotification: string -> unit,
         onTyping: int * int * string -> unit, onSeen: int * int * string -> unit,
         onProvider: ProviderDto -> unit) : Task =
        conn.On<JobDto>("JobUpdated", onJob) |> ignore
        conn.On<MessageDto>("MessageReceived", onMessage) |> ignore
        conn.On<LocationDto>("LocationUpdated", onLocation) |> ignore
        conn.On<string>("Notification", onNotification) |> ignore
        conn.On<ProviderDto>("ProviderUpdated", onProvider) |> ignore
        conn.On<int, int, string>("Typing", fun j s r -> onTyping (j, s, r)) |> ignore
        conn.On<int, int, string>("Seen", fun j s r -> onSeen (j, s, r)) |> ignore
        conn.StartAsync()

    member _.SendTyping (jobId: int) (senderId: int) (senderRole: string) : unit =
        if conn.State = HubConnectionState.Connected then
            fireAndForget (conn.InvokeAsync("SendTyping", jobId, senderId, senderRole))
    member _.SendSeen (jobId: int) (senderId: int) (senderRole: string) : unit =
        if conn.State = HubConnectionState.Connected then
            fireAndForget (conn.InvokeAsync("SendSeen", jobId, senderId, senderRole))
