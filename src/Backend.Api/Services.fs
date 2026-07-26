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
    /// The database was torn down and reseeded. Every connected client is now
    /// holding jobs that no longer exist — ids are not stable across a reseed —
    /// so this tells them to throw their world away and refetch. Broadcast to
    /// everyone for the same reason as ClockUpdated: it is not job-scoped and
    /// carries no one's data.
    abstract DataReset: unit -> Task
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
        member _.DataReset () = Task.CompletedTask

/// The job's reschedule columns as the domain type. Empty strings are the
/// absent case — see Dtos.JobDto for why the wire is flat rather than nested.
let readReschedule (j: Job) : Reschedule =
    let parse (s: string) = DateTimeOffset.Parse(s, Globalization.CultureInfo.InvariantCulture)
    let promised = if String.IsNullOrEmpty j.PromisedStart then parse j.ScheduledFor else parse j.PromisedStart
    let pending =
        if String.IsNullOrEmpty j.ProposedStart then None
        else
            ActorRole.ofWire j.ProposedBy
            |> Option.map (fun by ->
                { ProposedStart = parse j.ProposedStart
                  By = by
                  Reason = j.ProposalReason
                  ExpiresAt = parse j.ProposalExpiresAt })
    { PromisedStart = promised; Pending = pending }

let writeReschedule (j: Job) (r: Reschedule) : Job =
    match r.Pending with
    | Some p ->
        { j with
            PromisedStart = r.PromisedStart.ToString "o"
            ProposedStart = p.ProposedStart.ToString "o"
            ProposedBy = ActorRole.toWire p.By
            ProposalReason = p.Reason
            ProposalExpiresAt = p.ExpiresAt.ToString "o" }
    | None ->
        { j with
            PromisedStart = r.PromisedStart.ToString "o"
            ProposedStart = ""; ProposedBy = ""
            ProposalReason = ""; ProposalExpiresAt = "" }

/// The copy key clients switch on. Declined and lapsed leave identical state
/// behind but need different words, which is the whole reason the outcome is
/// carried separately from the new sub-status.
let outcomeName =
    function
    | ProposalRaised _ -> "ProposalRaised"
    | PromiseMoved _ -> "PromiseMoved"
    | PromiseStands _ -> "PromiseStands"
    | ProposalLapsed _ -> "ProposalLapsed"

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
      IsAccepted = j.IsAccepted
      CancelledBy = j.CancelledBy
      Lat = j.Lat; Lng = j.Lng; Address = j.Address }

type JobService(db: AppDb, hub: IBroadcaster) =
    member this.Apply (jobId: int) (event: JobEvent) : Task<Result<JobDto, string>> =
        this.ApplyBy jobId event None

    /// `by` is recorded only for Cancel; every other transition has exactly one
    /// party who can legally perform it, so storing an actor would be noise.
    member _.ApplyBy (jobId: int) (event: JobEvent) (by: ActorRole option) : Task<Result<JobDto, string>> =
        task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = jobId) |> Option.ofObj with
            | None -> return Error (sprintf "Job %d not found" jobId)
            | Some job ->
                match StateMachine.transition (JobStateCodec.toState job.State) event with
                | Error e -> return Error e
                | Ok next ->
                    // Turning up answers the question. A proposal is about when
                    // the provider will arrive, so once they have, it is moot —
                    // and leaving it pending renders a live "running late"
                    // negotiation on a job whose work is already underway.
                    let settled =
                        match next with
                        | Scheduled | EnRoute -> job
                        | _ -> writeReschedule job { readReschedule job with Pending = None }
                    let withActor =
                        match event, by with
                        | Cancel, Some role -> { settled with CancelledBy = ActorRole.toWire role }
                        // Assignment, recorded. The state stays Scheduled, so this
                        // flag is the only trace that the job is now taken.
                        | Accepted, _ -> { settled with IsAccepted = true }
                        | _ -> settled
                    let updated = { withActor with State = JobStateCodec.ofState next }
                    db.Entry(job).CurrentValues.SetValues(updated)
                    db.SaveChanges() |> ignore
                    let dto = toJobDto db updated
                    do! hub.JobUpdated dto
                    return Ok dto
        }

    /// The persistence half of `Reschedule.apply`.
    ///
    /// The pure function decides; this reads the sub-status out of the job's
    /// columns, hands it over, and writes back whatever comes out. Every rule —
    /// one proposal at a time, no promising the past, the proposer cannot
    /// answer itself — lives in Shared and is tested there, so this cannot
    /// disagree with the apps about what is legal.
    member _.Reschedule (jobId: int) (demoNow: DateTimeOffset) (ev: RescheduleEvent)
        : Task<Result<JobDto * RescheduleOutcome, string>> =
        task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = jobId) |> Option.ofObj with
            | None -> return Error (sprintf "Job %d not found" jobId)
            | Some job ->
                // Only a job still waiting for someone to turn up can be moved.
                match JobStateCodec.toState job.State with
                | Scheduled | EnRoute ->
                    let current = readReschedule job
                    match Reschedule.apply demoNow current ev with
                    | Error e -> return Error e
                    | Ok (next, outcome) ->
                        let updated = writeReschedule job next
                        db.Entry(job).CurrentValues.SetValues(updated)
                        db.SaveChanges() |> ignore
                        let dto = toJobDto db updated
                        do! hub.RescheduleChanged (dto, outcomeName outcome)
                        do! hub.JobUpdated dto
                        return Ok (dto, outcome)
                | other ->
                    return Error (sprintf "A job that is %A cannot be rescheduled." other)
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
                  // Just booked; no provider has taken it yet.
                  IsAccepted = false
                  CancelledBy = ""
                  Lat = resolvedLat; Lng = resolvedLng; Address = resolvedAddress }
            ignore prov
            db.Jobs.Add job |> ignore
            db.SaveChanges() |> ignore
            let dto = toJobDto db (db.Jobs.OrderByDescending(fun j -> j.Id).First())
            do! hub.JobUpdated dto
            return dto
        }
