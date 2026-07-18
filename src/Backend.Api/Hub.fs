module FixItHere.Backend.Hub

open System.Threading.Tasks

open Microsoft.AspNetCore.SignalR

open FixItHere.Shared.Dtos
open FixItHere.Backend.Services

type DemoHub() =
    inherit Hub()
    member this.SendTyping(jobId: int, senderId: int) : Task =
        this.Clients.Others.SendAsync("Typing", jobId, senderId)
    member this.SendSeen(jobId: int, senderId: int) : Task =
        this.Clients.Others.SendAsync("Seen", jobId, senderId)

type SignalRBroadcaster(ctx: IHubContext<DemoHub>) =
    interface IBroadcaster with
        member _.JobUpdated dto = ctx.Clients.All.SendAsync("JobUpdated", dto)
        member _.MessageReceived dto = ctx.Clients.All.SendAsync("MessageReceived", dto)
        member _.LocationUpdated dto = ctx.Clients.All.SendAsync("LocationUpdated", dto)
        member _.Notify text = ctx.Clients.All.SendAsync("Notification", text)
        member _.ProviderUpdated dto = ctx.Clients.All.SendAsync("ProviderUpdated", dto)
