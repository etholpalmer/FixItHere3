module FixItHere.Backend.Services

open System.Linq
open System
open System.Threading.Tasks
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db

type IBroadcaster =
    /// JobDto carries both parties, so this targets without a lookup.
    abstract JobUpdated: JobDto -> Task
    /// MessageDto carries only the sender, so the caller — which already has the
    /// job in hand — passes both parties rather than making this re-query.
    abstract MessageReceived: MessageDto * customerId: int * providerId: int -> Task
    /// Notifications are about a job, so they go to that job's parties. A bare
    /// broadcast toasted every connected client with other people's events.
    abstract NotifyJob: text: string * customerId: int * providerId: int -> Task
    abstract LocationUpdated: LocationDto -> Task
    abstract ProviderUpdated: ProviderDto -> Task
    /// The demo clock changed shape (paused, resumed, re-rated, jumped).
    /// Broadcast to everyone on purpose: it is not job-scoped, every client
    /// needs it, and it carries no one's data.
    abstract ClockUpdated: DemoClockDto -> Task
    /// A reschedule negotiation moved. The job DTO carries the whole new
    /// sub-status, so the client re-renders rather than patching; `outcome` is
    /// the copy key ("ProposalRaised" | "PromiseMoved" | "PromiseStands" |
    /// "ProposalLapsed"), which is what makes decline and lapse readable as
    /// different events despite leaving identical state behind.
    abstract RescheduleChanged: JobDto * outcome: string -> Task

type NullBroadcaster() =
    interface IBroadcaster with
        member _.JobUpdated _ = Task.CompletedTask
        member _.MessageReceived (_, _, _) = Task.CompletedTask
        member _.NotifyJob (_, _, _) = Task.CompletedTask
        member _.LocationUpdated _ = Task.CompletedTask
        member _.ProviderUpdated _ = Task.CompletedTask
        member _.ClockUpdated _ = Task.CompletedTask
        member _.RescheduleChanged (_, _) = Task.CompletedTask

let toJobDto (db: AppDb) (j: Job) : JobDto =
    let cust = db.Customers.Single(fun c -> c.Id = j.CustomerId)
    let prov = db.Providers.Single(fun p -> p.Id = j.ProviderId)
    let svc  = db.Services.Single(fun s -> s.Id = j.ServiceId)
    { Id = j.Id; CustomerId = j.CustomerId; CustomerName = cust.Name
      ProviderId = j.ProviderId; ProviderName = prov.BusinessName
      ServiceId = j.ServiceId; ServiceName = svc.Name
      State = j.State; Price = j.Price; ScheduledFor = j.ScheduledFor
      // The promise falls back to the booking when no reschedule has moved it.
      // Never empty: every countdown targets this field, and an empty target
      // renders as a blank where a time should be.
      PromisedStart = (if System.String.IsNullOrEmpty j.PromisedStart then j.ScheduledFor else j.PromisedStart)
      ProposedStart = j.ProposedStart; ProposedBy = j.ProposedBy
      ProposalReason = j.ProposalReason; ProposalExpiresAt = j.ProposalExpiresAt
      IsDemoTracked = j.IsDemoTracked
      Lat = j.Lat; Lng = j.Lng; Address = j.Address }

type JobService(db: AppDb, hub: IBroadcaster) =
    member _.Apply (jobId: int) (event: JobEvent) : Task<Result<JobDto, string>> =
        task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = jobId) |> Option.ofObj with
            | None -> return Error (sprintf "Job %d not found" jobId)
            | Some job ->
                match StateMachine.transition (JobStateCodec.toState job.State) event with
                | Error e -> return Error e
                | Ok next ->
                    let updated = { job with State = JobStateCodec.ofState next }
                    db.Entry(job).CurrentValues.SetValues(updated)
                    db.SaveChanges() |> ignore
                    let dto = toJobDto db updated
                    do! hub.JobUpdated dto
                    return Ok dto
        }

    /// `startsAt` is resolved by the caller against the demo clock rather than
    /// read from the request: the service has no clock, and the label ("Now")
    /// is the customer's word for a time, not the time itself.
    member _.Create (req: CreateJobRequest) (startsAt: DateTimeOffset) : Task<JobDto> =
        task {
            let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
            // The app cannot know the customer's street address — it only has a
            // coordinate — so it sends the placeholder "My location" and the
            // customer record supplies the real one. Without this a booked job
            // reads "Address: My location" on the provider's screen, which is
            // the one job the demo audience actually watches.
            let svcName = db.Services.Single(fun sv -> sv.Id = req.ServiceId).Name
            let cust = db.Customers.SingleOrDefault(fun c -> c.Id = req.CustomerId)
            let resolvedAddress, resolvedLat, resolvedLng =
                if obj.ReferenceEquals(cust, null) then req.Address, req.Lat, req.Lng
                else cust.Address, cust.Lat, cust.Lng
            let job =
                { Id = 0; CustomerId = req.CustomerId; ProviderId = req.ProviderId
                  ServiceId = req.ServiceId; State = "Scheduled"
                  // Priced from the trade rather than a flat rate: a plumbing
                  // call and a house clean costing the same is a tell.
                  Price = ServiceRate.quote svcName
                  ScheduledFor = startsAt.ToString "o"
                  PromisedStart = startsAt.ToString "o"
                  ProposedStart = ""; ProposedBy = ""
                  ProposalReason = ""; ProposalExpiresAt = ""
                  // Booked in-session, so this one *is* the demo.
                  IsDemoTracked = true
                  Lat = resolvedLat; Lng = resolvedLng; Address = resolvedAddress }
            ignore prov
            db.Jobs.Add job |> ignore
            db.SaveChanges() |> ignore
            let dto = toJobDto db (db.Jobs.OrderByDescending(fun j -> j.Id).First())
            do! hub.JobUpdated dto
            return dto
        }
