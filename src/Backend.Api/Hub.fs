module FixItHere.Backend.Hub

open System.Threading.Tasks

open Microsoft.AspNetCore.SignalR

open FixItHere.Shared.Dtos
open FixItHere.Backend.Services

/// Group name for one actor. Customer 1 and Provider 1 are different actors, so
/// the role is part of the key — the same reason MessageDto carries SenderRole.
let actorGroup (role: string) (id: int) = sprintf "%s-%d" role id

/// The /dev console joins this to keep its deliberate firehose view.
let consoleGroup = "dev-console"

type DemoHub() =
    inherit Hub()

    /// Clients call this after connecting AND after every reconnect — SignalR
    /// group membership is NOT preserved across a reconnect, so skipping the
    /// second call gives a client that silently stops receiving its own events.
    member this.JoinActor(role: string, id: int) : Task =
        this.Groups.AddToGroupAsync(this.Context.ConnectionId, actorGroup role id)

    member this.JoinConsole() : Task =
        this.Groups.AddToGroupAsync(this.Context.ConnectionId, consoleGroup)

    // senderRole disambiguates the id: customer 1 and provider 1 are different actors.
    member this.SendTyping(jobId: int, senderId: int, senderRole: string) : Task =
        this.Clients.Others.SendAsync("Typing", jobId, senderId, senderRole)
    member this.SendSeen(jobId: int, senderId: int, senderRole: string) : Task =
        this.Clients.Others.SendAsync("Seen", jobId, senderId, senderRole)

type SignalRBroadcaster(ctx: IHubContext<DemoHub>) =
    /// The two parties to a job, plus the console. Anything job-scoped goes here
    /// rather than to Clients.All: broadcasting JobUpdated to everyone meant one
    /// customer's booking appeared in every other customer's job list, because
    /// the apps append any job id they have not seen before.
    let jobParties (customerId: int) (providerId: int) =
        ResizeArray [ actorGroup "Customer" customerId
                      actorGroup "Provider" providerId
                      consoleGroup ]

    interface IBroadcaster with
        member _.JobUpdated dto =
            ctx.Clients.Groups(jobParties dto.CustomerId dto.ProviderId)
               .SendAsync("JobUpdated", dto)
        member _.MessageReceived (dto, customerId, providerId) =
            ctx.Clients.Groups(jobParties customerId providerId)
               .SendAsync("MessageReceived", dto)
        member _.NotifyJob (text, customerId, providerId) =
            ctx.Clients.Groups(jobParties customerId providerId)
               .SendAsync("Notification", text)
        // Deliberately NOT job-scoped:
        //   LocationUpdated is per-provider, not per-job, and drives no toast —
        //     targeting it would need a lookup for no visible gain.
        //   ProviderUpdated is catalogue freshness every browsing customer wants.
        member _.LocationUpdated dto = ctx.Clients.All.SendAsync("LocationUpdated", dto)
        member _.ProviderUpdated dto = ctx.Clients.All.SendAsync("ProviderUpdated", dto)
