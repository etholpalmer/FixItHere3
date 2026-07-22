module FixItHere.Backend.Endpoints

open System
open System.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

[<CLIMutable>]
type SetOnlineRequest = { Online: bool }

let private okJson (data: 't) = Results.Json(Envelope.ok data)
let private err (status: int) (msg: string) =
    Results.Json(Envelope.fail msg, statusCode = status)

let private haversineKm = Geo.distanceKm

let private toProviderDto (db: AppDb) (p: Provider) : ProviderDto =
    let svcName = db.Services.Single(fun s -> s.Id = p.ServiceId).Name
    // Filter on (id, role): customer and provider ids overlap, so RateeId alone
    // would fold ratings *about a customer* into this provider's average.
    let ratings =
        db.Ratings.Where(fun r -> r.RateeId = p.Id && r.RateeRole = "Provider")
                  .Select(fun r -> r.Stars).ToList()
    { Id = p.Id; BusinessName = p.BusinessName
      ServiceId = p.ServiceId; ServiceName = svcName
      Rating = (if ratings.Count = 0 then 0.0 else ratings |> Seq.averageBy float)
      RatingCount = ratings.Count
      Lat = p.Lat; Lng = p.Lng; Online = p.Online
      Vehicle = p.Vehicle; PhotoUrl = p.PhotoUrl }

let mapAll (app: WebApplication) =

    // ---- Demo clock -------------------------------------------------------
    // GET is the resync path, and it exists for two distinct moments: app
    // startup, and every SignalR reconnect. A client that missed a
    // ClockUpdated while disconnected cannot reconstruct the map from the
    // ticks it did not receive — it has to ask.
    app.MapGet("/demo/clock", Func<Clock.DemoClockService, IResult>(fun clock ->
        okJson (Clock.toDto clock.Current DateTimeOffset.UtcNow))) |> ignore

    app.MapPost("/demo/clock",
        Func<SetClockRequest, Clock.DemoClockService, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
            fun req clock hub -> task {
                match Clock.interpret req with
                | Error msg -> return err 400 msg
                | Ok mutate ->
                    let updated = clock.Apply mutate
                    let dto = Clock.toDto updated DateTimeOffset.UtcNow
                    do! hub.ClockUpdated dto
                    return okJson dto })) |> ignore


    // Demo auth (see Auth.fs): real *shape* and real failure modes, no security.
    // Distinguishing "unknown account" from "wrong password" is what makes a
    // sign-in feel real when someone tests it; both are 401 to the user.
    app.MapPost("/login", Func<LoginRequest, AppDb, IResult>(fun req db ->
        let email = if isNull req.Email then "" else req.Email.Trim().ToLowerInvariant()
        let passwordOk = req.Password = Auth.passwordFor req.Role
        match req.Role with
        | "Customer" ->
            match db.Customers.SingleOrDefault(fun c -> c.Email = email) |> Option.ofObj with
            | None -> err 401 "No account found for that email"
            | Some _ when not passwordOk -> err 401 "Incorrect password"
            | Some c ->
                okJson { Token = sprintf "fake-customer-%d" c.Id
                         UserId = c.Id; Role = "Customer"; DisplayName = c.Name }
        | "Provider" ->
            match db.Providers.SingleOrDefault(fun p -> p.Email = email) |> Option.ofObj with
            | None -> err 401 "No account found for that email"
            | Some _ when not passwordOk -> err 401 "Incorrect password"
            | Some p ->
                okJson { Token = sprintf "fake-provider-%d" p.Id
                         UserId = p.Id; Role = "Provider"; DisplayName = p.BusinessName }
        | r -> err 400 (sprintf "Unknown role %s" r))) |> ignore

    // Avatars are generated, not served from disk: the seed used to point at
    // /img/provider-N.png files that were never shipped, so every avatar 404'd.
    // Generating them means a valid id can never miss.
    let avatarResult (name: string option) =
        match name with
        | Some n -> Results.Content(Auth.avatarSvg n, "image/svg+xml")
        | None -> Results.Content(Auth.avatarSvg "?", "image/svg+xml")

    app.MapGet("/avatar/provider/{id}.svg", Func<AppDb, int, IResult>(fun db id ->
        db.Providers.SingleOrDefault(fun p -> p.Id = id) |> Option.ofObj
        |> Option.map (fun p -> p.BusinessName) |> avatarResult)) |> ignore

    app.MapGet("/avatar/customer/{id}.svg", Func<AppDb, int, IResult>(fun db id ->
        db.Customers.SingleOrDefault(fun c -> c.Id = id) |> Option.ofObj
        |> Option.map (fun c -> c.Name) |> avatarResult)) |> ignore

    app.MapGet("/customers", Func<AppDb, IResult>(fun db ->
        okJson (db.Customers.OrderBy(fun c -> c.Id)
                |> Seq.map (fun c -> { Id = c.Id; Name = c.Name; Email = c.Email })
                |> List.ofSeq))) |> ignore

    app.MapGet("/services", Func<AppDb, IResult>(fun db ->
        okJson (db.Services.OrderBy(fun s -> s.Id)
                |> Seq.map (fun s ->
                    let r = ServiceRate.forService s.Name
                    { Id = s.Id; Name = s.Name
                      FromPrice = ServiceRate.quote s.Name
                      TypicalMinutes = r.TypicalMinutes })
                |> List.ofSeq))) |> ignore

    app.MapGet("/providers", Func<AppDb, Nullable<int>, Nullable<float>, Nullable<float>, IResult>(
        fun db serviceId lat lng ->
            let q =
                if serviceId.HasValue then
                    let sid = serviceId.Value
                    db.Providers.Where(fun p -> p.ServiceId = sid)
                else db.Providers.AsQueryable()
            let dtos = q |> Seq.map (toProviderDto db) |> List.ofSeq
            let sorted =
                if lat.HasValue && lng.HasValue then
                    dtos |> List.sortBy (fun p -> haversineKm (lat.Value, lng.Value) (p.Lat, p.Lng))
                else dtos
            okJson sorted)) |> ignore

    app.MapGet("/providers/{id}", Func<AppDb, int, IResult>(fun db id ->
        match db.Providers.SingleOrDefault(fun p -> p.Id = id) |> Option.ofObj with
        | Some p -> okJson (toProviderDto db p)
        | None -> err 404 (sprintf "Provider %d not found" id))) |> ignore

    app.MapPut("/providers/{id}/online",
        Func<int, SetOnlineRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
            fun id req db hub -> task {
                match db.Providers.SingleOrDefault(fun p -> p.Id = id) |> Option.ofObj with
                | None -> return err 404 (sprintf "Provider %d not found" id)
                | Some prov ->
                    let updated = { prov with Online = req.Online }
                    db.Entry(prov).CurrentValues.SetValues(updated)
                    db.SaveChanges() |> ignore
                    let dto = toProviderDto db updated
                    do! hub.ProviderUpdated dto
                    return okJson dto })) |> ignore

    app.MapPost("/jobs",
        Func<CreateJobRequest, JobService, Clock.DemoClockService, System.Threading.Tasks.Task<IResult>>(
            fun req svc clock -> task {
                // An unknown slot is a 400, not a silent fallback to "as soon as
                // possible": defaulting would turn a typo into a job booked
                // twelve minutes out, which is a wrong answer wearing the shape
                // of a right one.
                match BookingSlot.tryResolve req.ScheduleChoice (clock.Now()) with
                | None ->
                    return err 400 (sprintf "Unknown schedule '%s'. Expected one of: %s"
                                        req.ScheduleChoice (String.Join(", ", BookingSlot.options)))
                | Some startsAt ->
                    let! dto = svc.Create req startsAt
                    return okJson dto })) |> ignore

    // ---- Reschedule and no-show ------------------------------------------
    // Bilateral by construction: the caller states which party it is, and
    // `Reschedule.apply` refuses to let the proposing party answer itself.

    app.MapPost("/jobs/reschedule",
        Func<ProposeRescheduleRequest, JobService, Clock.DemoClockService, System.Threading.Tasks.Task<IResult>>(
            fun req svc clock -> task {
                match ActorRole.ofWire req.ByRole with
                | None -> return err 400 (sprintf "Unknown role '%s'." req.ByRole)
                | Some by ->
                    match DateTimeOffset.TryParse(req.ProposedStart, Globalization.CultureInfo.InvariantCulture,
                                                  Globalization.DateTimeStyles.RoundtripKind) with
                    | false, _ -> return err 400 (sprintf "Cannot parse '%s' as a time." req.ProposedStart)
                    | true, proposedStart ->
                        let now = clock.Now()
                        let proposal =
                            { ProposedStart = proposedStart
                              By = by
                              Reason = (if String.IsNullOrWhiteSpace req.Reason then "No reason given" else req.Reason)
                              // Expiry is set here, not by the caller: a client
                              // choosing its own window could keep a proposal
                              // alive indefinitely and strand the other party.
                              ExpiresAt = now + Reschedule.proposalWindow }
                        match! svc.Reschedule req.JobId now (Propose proposal) with
                        | Ok (dto, _) -> return okJson dto
                        | Error e -> return err 409 e })) |> ignore

    app.MapPost("/jobs/reschedule/decision",
        Func<RescheduleDecisionRequest, JobService, Clock.DemoClockService, System.Threading.Tasks.Task<IResult>>(
            fun req svc clock -> task {
                match ActorRole.ofWire req.ByRole with
                | None -> return err 400 (sprintf "Unknown role '%s'." req.ByRole)
                | Some by ->
                    let ev = if req.Accept then AcceptProposal by else DeclineProposal by
                    match! svc.Reschedule req.JobId (clock.Now()) ev with
                    | Ok (dto, _) -> return okJson dto
                    | Error e -> return err 409 e })) |> ignore

    app.MapPost("/jobs/no-show",
        // IBroadcaster is scoped, so it must arrive as a handler parameter.
        // Reaching for app.Services.GetRequiredService here resolved from the
        // *root* provider and threw at request time — a 500 that still applied
        // the state change, so the job looked correctly transitioned to anyone
        // checking afterwards.
        Func<ReportNoShowRequest, JobService, AppDb, Clock.DemoClockService, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
            fun req svc db clock hub -> task {
                match db.Jobs.SingleOrDefault(fun j -> j.Id = req.JobId) |> Option.ofObj with
                | None -> return err 404 (sprintf "Job %d not found" req.JobId)
                | Some job ->
                    // Gated on the clock, not on a button being visible. The
                    // grace window is the rule; the UI merely reflects it.
                    let sched = readReschedule job
                    if not (Reschedule.canReportNoShow (clock.Now()) sched) then
                        return err 409
                            (sprintf "Too early: a no-show can only be reported after %s."
                                ((Reschedule.noShowDeadline sched).ToString "o"))
                    else
                        match! svc.Apply req.JobId MarkNoShow with
                        | Ok dto ->
                            do! hub.NotifyJob ("Reported as a no-show", dto.CustomerId, dto.ProviderId)
                            return okJson dto
                        | Error e -> return err 409 e })) |> ignore

    app.MapGet("/jobs", Func<AppDb, Nullable<int>, Nullable<int>, IResult>(
        fun db customerId providerId ->
            let q =
                if customerId.HasValue then
                    let cid = customerId.Value
                    db.Jobs.Where(fun j -> j.CustomerId = cid)
                elif providerId.HasValue then
                    let pid = providerId.Value
                    db.Jobs.Where(fun j -> j.ProviderId = pid)
                else db.Jobs.AsQueryable()
            okJson (q |> Seq.map (toJobDto db) |> List.ofSeq))) |> ignore

    app.MapGet("/jobs/{id}", Func<AppDb, int, IResult>(fun db id ->
        match db.Jobs.SingleOrDefault(fun j -> j.Id = id) |> Option.ofObj with
        | Some j -> okJson (toJobDto db j)
        | None -> err 404 (sprintf "Job %d not found" id))) |> ignore

    let mapTransition (path: string) (event: JobEvent) =
        app.MapPut(sprintf "/jobs/{id}/%s" path,
            Func<int, JobService, System.Threading.Tasks.Task<IResult>>(fun id svc -> task {
                match! svc.Apply id event with
                | Ok dto -> return okJson dto
                | Error msg when msg.Contains "not found" -> return err 404 msg
                | Error msg -> return err 409 msg })) |> ignore
    mapTransition "accept"   Accepted
    mapTransition "enroute"  DepartEnRoute
    mapTransition "arrive"   Arrive
    mapTransition "start"    StartWork
    mapTransition "complete" CompleteWork
    mapTransition "cancel"   Cancel

    app.MapGet("/messages", Func<AppDb, int, IResult>(fun db jobId ->
        okJson (db.Messages.Where(fun m -> m.JobId = jobId).OrderBy(fun m -> m.Id)
                |> Seq.map (fun m ->
                    // Resolve by role: customer and provider ids overlap (both 1..N),
                    // so a lookup that tried Customers first would shadow every provider.
                    let sender =
                        if m.SenderRole = "Provider" then
                            db.Providers.SingleOrDefault(fun p -> p.Id = m.SenderId) |> Option.ofObj
                            |> Option.map (fun p -> p.BusinessName)
                            |> Option.defaultValue "Unknown"
                        else
                            db.Customers.SingleOrDefault(fun c -> c.Id = m.SenderId) |> Option.ofObj
                            |> Option.map (fun c -> c.Name)
                            |> Option.defaultValue "Unknown"
                    { Id = m.Id; JobId = m.JobId; SenderId = m.SenderId
                      SenderRole = m.SenderRole; SenderName = sender
                      Text = m.Text; PhotoBase64 = m.PhotoBase64; SentAt = m.SentAt; Seen = m.Seen })
                |> List.ofSeq))) |> ignore

    app.MapPost("/messages", Func<SendMessageRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            let senderRole = if req.SenderRole = "Provider" then "Provider" else "Customer"
            let msg =
                { Id = 0; JobId = req.JobId; SenderId = req.SenderId; SenderRole = senderRole
                  Text = req.Text; PhotoBase64 = req.PhotoBase64
                  SentAt = FixItHere.Backend.Seed.nowIso (); Seen = false }
            db.Messages.Add msg |> ignore
            db.SaveChanges() |> ignore
            let saved = db.Messages.OrderByDescending(fun m -> m.Id).First()
            let senderName =
                if senderRole = "Provider" then
                    db.Providers.SingleOrDefault(fun p -> p.Id = saved.SenderId) |> Option.ofObj
                    |> Option.map (fun p -> p.BusinessName) |> Option.defaultValue "Unknown"
                else
                    db.Customers.SingleOrDefault(fun c -> c.Id = saved.SenderId) |> Option.ofObj
                    |> Option.map (fun c -> c.Name) |> Option.defaultValue "Unknown"
            let dto =
                { Id = saved.Id; JobId = saved.JobId; SenderId = saved.SenderId
                  SenderRole = saved.SenderRole; SenderName = senderName
                  Text = saved.Text; PhotoBase64 = saved.PhotoBase64
                  SentAt = saved.SentAt; Seen = saved.Seen }
            // The job supplies both parties; MessageDto carries only the sender.
            let job = db.Jobs.SingleOrDefault(fun j -> j.Id = saved.JobId)
            if not (obj.ReferenceEquals(job, null)) then
                do! hub.MessageReceived (dto, job.CustomerId, job.ProviderId)
            return okJson dto })) |> ignore

    app.MapGet("/ratings", Func<AppDb, int, IResult>(fun db providerId ->
        // Same (id, role) filter as toProviderDto — this is the provider's
        // public feedback, so ratings about a customer must not appear here.
        okJson (db.Ratings.Where(fun r -> r.RateeId = providerId && r.RateeRole = "Provider")
                |> Seq.map (fun r ->
                    // Resolve by (id, role): the two id spaces overlap.
                    let raterName =
                        if r.RaterRole = "Provider" then
                            db.Providers.SingleOrDefault(fun p -> p.Id = r.RaterId) |> Option.ofObj
                            |> Option.map (fun p -> p.BusinessName) |> Option.defaultValue "Unknown"
                        else
                            db.Customers.SingleOrDefault(fun c -> c.Id = r.RaterId) |> Option.ofObj
                            |> Option.map (fun c -> c.Name) |> Option.defaultValue "Unknown"
                    { Id = r.Id; JobId = r.JobId
                      RaterId = r.RaterId; RaterRole = r.RaterRole; RaterName = raterName
                      RateeId = r.RateeId; RateeRole = r.RateeRole
                      Stars = r.Stars; Comment = r.Comment; CreatedAt = r.CreatedAt })
                |> List.ofSeq))) |> ignore

    app.MapPost("/ratings", Func<CreateRatingRequest, AppDb, JobService, System.Threading.Tasks.Task<IResult>>(
        fun req db svc -> task {
            // Normalise the roles rather than trusting the payload verbatim: an
            // absent role would otherwise persist as null and match no filter,
            // silently hiding the rating from both directions.
            let norm (r: string) = if r = "Provider" then "Provider" else "Customer"
            let rating =
                { Id = 0; JobId = req.JobId
                  RaterId = req.RaterId; RaterRole = norm req.RaterRole
                  RateeId = req.RateeId; RateeRole = norm req.RateeRole
                  Stars = req.Stars; Comment = req.Comment
                  CreatedAt = Seed.nowIso () }
            db.Ratings.Add rating |> ignore
            db.SaveChanges() |> ignore
            // Rating a completed job closes it (simplified single-sided close for the demo)
            let job = db.Jobs.SingleOrDefault(fun j -> j.Id = req.JobId)
            if not (obj.ReferenceEquals(job, null)) && job.State = "Completed" then
                let! _ = svc.Apply req.JobId RateAndClose
                ()
            let saved = db.Ratings.OrderByDescending(fun r -> r.Id).First()
            return okJson
                { Id = saved.Id; JobId = saved.JobId
                  RaterId = saved.RaterId; RaterRole = saved.RaterRole; RaterName = ""
                  RateeId = saved.RateeId; RateeRole = saved.RateeRole
                  Stars = saved.Stars; Comment = saved.Comment
                  CreatedAt = saved.CreatedAt } })) |> ignore

    app.MapGet("/location", Func<AppDb, int, IResult>(fun db providerId ->
        match db.Providers.SingleOrDefault(fun p -> p.Id = providerId) |> Option.ofObj with
        | Some p ->
            okJson { ProviderId = p.Id; Lat = p.Lat; Lng = p.Lng
                     UpdatedAt = FixItHere.Backend.Seed.nowIso () }
        | None -> err 404 (sprintf "Provider %d not found" providerId))) |> ignore

    app.MapPut("/location", Func<UpdateLocationRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            match db.Providers.SingleOrDefault(fun p -> p.Id = req.ProviderId) |> Option.ofObj with
            | None -> return err 404 (sprintf "Provider %d not found" req.ProviderId)
            | Some prov ->
                let updated = { prov with Lat = req.Lat; Lng = req.Lng }
                db.Entry(prov).CurrentValues.SetValues(updated)
                db.SaveChanges() |> ignore
                let dto = { ProviderId = prov.Id; Lat = req.Lat; Lng = req.Lng
                            UpdatedAt = FixItHere.Backend.Seed.nowIso () }
                do! hub.LocationUpdated dto
                return okJson dto })) |> ignore

    app.MapPost("/payment/simulate", Func<PaymentRequest, AppDb, IBroadcaster, System.Threading.Tasks.Task<IResult>>(
        fun req db hub -> task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = req.JobId) |> Option.ofObj with
            | None -> return err 404 (sprintf "Job %d not found" req.JobId)
            | Some job ->
                let serviceName =
                    db.Services.SingleOrDefault(fun s -> s.Id = job.ServiceId) |> Option.ofObj
                    |> Option.map (fun s -> s.Name) |> Option.defaultValue ""
                let rate = ServiceRate.forService serviceName
                // Derive the lines *from the agreed price* rather than recomputing
                // the quote: the receipt must reconcile to what the customer was
                // shown at booking, even if the rate card later changes.
                let callOut = min rate.CallOutFee job.Price
                let labour = job.Price - callOut
                // Recover the *billed* minutes from the labour amount rather than
                // quoting the trade's typical duration: the seed jitters duration
                // around the typical, so "Labour (1h 30m) $206.25" at $125/h is a
                // line a careful reader can catch not adding up.
                let labourMinutes =
                    if rate.HourlyRate <= 0m then 0
                    else int (System.Math.Round(labour / rate.HourlyRate * 60m))
                let lines = Money.breakdown callOut labourMinutes labour
                let card =
                    db.Customers.SingleOrDefault(fun c -> c.Id = job.CustomerId) |> Option.ofObj
                    |> Option.map (fun c -> sprintf "%s ****%s" c.CardBrand c.CardLast4)
                    |> Option.defaultValue "Card on file"
                do! hub.NotifyJob ("Payment Complete", job.CustomerId, job.ProviderId)
                return okJson
                    { JobId = job.Id
                      CallOutFee = lines.CallOutFee; LabourMinutes = lines.LabourMinutes
                      LabourAmount = lines.LabourAmount
                      Subtotal = lines.Subtotal; Tax = lines.Tax; Amount = lines.Total
                      PlatformFee = lines.PlatformFee; ProviderPayout = lines.ProviderPayout
                      Method = card; Status = "Transferred" } })) |> ignore
