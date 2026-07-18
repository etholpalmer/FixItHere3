module FixItHere.ClientShared.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR.Client
open FixItHere.Shared.Dtos

type HubClient(baseUrl: string) =
    let conn =
        HubConnectionBuilder().WithUrl(baseUrl + "/hub").WithAutomaticReconnect().Build()

    member _.Start
        (onJob: JobDto -> unit, onMessage: MessageDto -> unit,
         onLocation: LocationDto -> unit, onNotification: string -> unit,
         onTyping: int * int -> unit, onSeen: int * int -> unit) : Task =
        conn.On<JobDto>("JobUpdated", onJob) |> ignore
        conn.On<MessageDto>("MessageReceived", onMessage) |> ignore
        conn.On<LocationDto>("LocationUpdated", onLocation) |> ignore
        conn.On<string>("Notification", onNotification) |> ignore
        conn.On<int, int>("Typing", fun j s -> onTyping (j, s)) |> ignore
        conn.On<int, int>("Seen", fun j s -> onSeen (j, s)) |> ignore
        conn.StartAsync()

    member _.SendTyping (jobId: int) (senderId: int) : unit =
        if conn.State = HubConnectionState.Connected then
            conn.InvokeAsync("SendTyping", jobId, senderId) |> ignore
    member _.SendSeen (jobId: int) (senderId: int) : unit =
        if conn.State = HubConnectionState.Connected then
            conn.InvokeAsync("SendSeen", jobId, senderId) |> ignore
