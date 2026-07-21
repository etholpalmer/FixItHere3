module FixItHere.Backend.Services

open System.Linq
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

type NullBroadcaster() =
    interface IBroadcaster with
        member _.JobUpdated _ = Task.CompletedTask
        member _.MessageReceived (_, _, _) = Task.CompletedTask
        member _.NotifyJob (_, _, _) = Task.CompletedTask
        member _.LocationUpdated _ = Task.CompletedTask
        member _.ProviderUpdated _ = Task.CompletedTask

let toJobDto (db: AppDb) (j: Job) : JobDto =
    let cust = db.Customers.Single(fun c -> c.Id = j.CustomerId)
    let prov = db.Providers.Single(fun p -> p.Id = j.ProviderId)
    let svc  = db.Services.Single(fun s -> s.Id = j.ServiceId)
    { Id = j.Id; CustomerId = j.CustomerId; CustomerName = cust.Name
      ProviderId = j.ProviderId; ProviderName = prov.BusinessName
      ServiceId = j.ServiceId; ServiceName = svc.Name
      State = j.State; Price = j.Price; ScheduledFor = j.ScheduledFor
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

    member _.Create (req: CreateJobRequest) : Task<JobDto> =
        task {
            let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
            let job =
                { Id = 0; CustomerId = req.CustomerId; ProviderId = req.ProviderId
                  ServiceId = req.ServiceId; State = "Scheduled"
                  Price = 85.00m
                  ScheduledFor = req.ScheduleChoice
                  Lat = req.Lat; Lng = req.Lng; Address = req.Address }
            ignore prov
            db.Jobs.Add job |> ignore
            db.SaveChanges() |> ignore
            let dto = toJobDto db (db.Jobs.OrderByDescending(fun j -> j.Id).First())
            do! hub.JobUpdated dto
            return dto
        }
