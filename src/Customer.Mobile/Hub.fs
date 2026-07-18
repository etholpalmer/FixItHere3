module FixItHere.Customer.Hub

open System.Threading.Tasks
open Microsoft.AspNetCore.SignalR.Client
open FixItHere.Shared.Dtos
open FixItHere.Customer

type HubClient(baseUrl: string) =
    let conn =
        HubConnectionBuilder()
            .WithUrl(baseUrl + "/hub")
            .WithAutomaticReconnect()
            .Build()

    /// Registers the four server events and starts the connection.
    member _.Start(dispatch: Msg -> unit) : Task =
        conn.On<JobDto>("JobUpdated", fun dto -> dispatch (HubJobUpdated dto)) |> ignore
        conn.On<MessageDto>("MessageReceived", fun dto -> dispatch (HubMessageReceived dto)) |> ignore
        conn.On<LocationDto>("LocationUpdated", fun dto -> dispatch (HubLocationUpdated dto)) |> ignore
        conn.On<string>("Notification", fun text -> dispatch (HubNotification text)) |> ignore
        conn.StartAsync()
