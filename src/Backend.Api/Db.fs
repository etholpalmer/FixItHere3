namespace FixItHere.Backend.Db

open Microsoft.EntityFrameworkCore
open FixItHere.Shared

[<CLIMutable>]
type Service = { Id: int; Name: string }

[<CLIMutable>]
type Customer = { Id: int; Name: string; Email: string; Lat: float; Lng: float }

[<CLIMutable>]
type Provider =
    { Id: int; BusinessName: string; Email: string; ServiceId: int
      Lat: float; Lng: float; Online: bool; Vehicle: string; PhotoUrl: string }

[<CLIMutable>]
type Job =
    { Id: int; CustomerId: int; ProviderId: int; ServiceId: int
      State: string; Price: decimal; ScheduledFor: string
      Lat: float; Lng: float; Address: string }

[<CLIMutable>]
type Message =
    { Id: int; JobId: int; SenderId: int; SenderRole: string; Text: string
      PhotoBase64: string; SentAt: string; Seen: bool }

[<CLIMutable>]
type Rating =
    { Id: int; JobId: int
      RaterId: int; RaterRole: string
      RateeId: int; RateeRole: string
      Stars: int; Comment: string }

module JobStateCodec =
    let ofState (s: JobState) = sprintf "%A" s
    let toState (s: string) : JobState =
        match s with
        | "Scheduled" -> Scheduled | "EnRoute" -> EnRoute | "Arrived" -> Arrived
        | "InProgress" -> InProgress | "Completed" -> Completed
        | "Closed" -> Closed | "Cancelled" -> Cancelled
        | other -> failwithf "Unknown job state '%s'" other

type AppDb(options: DbContextOptions<AppDb>) =
    inherit DbContext(options)

    // Register entities explicitly — robust across F#/EF Core versions instead
    // of relying on DbSet-property auto-discovery.
    override _.OnModelCreating(modelBuilder: ModelBuilder) =
        modelBuilder.Entity<Service>()  |> ignore
        modelBuilder.Entity<Customer>() |> ignore
        modelBuilder.Entity<Provider>() |> ignore
        modelBuilder.Entity<Job>()      |> ignore
        modelBuilder.Entity<Message>()  |> ignore
        modelBuilder.Entity<Rating>()   |> ignore

    // Computed sets via Set<T>() — never null, no auto-init dependency.
    member this.Services  : DbSet<Service>  = this.Set<Service>()
    member this.Customers : DbSet<Customer> = this.Set<Customer>()
    member this.Providers : DbSet<Provider> = this.Set<Provider>()
    member this.Jobs      : DbSet<Job>      = this.Set<Job>()
    member this.Messages  : DbSet<Message>  = this.Set<Message>()
    member this.Ratings   : DbSet<Rating>   = this.Set<Rating>()
