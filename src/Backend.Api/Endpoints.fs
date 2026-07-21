module FixItHere.Backend.Endpoints

open System
open System.Linq
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

[<CLIMutable>]
type SetOnlineRequest = { Online: bool }

let private okJson (data: 't) = Results.Json(Envelope.ok data)
let private err (status: int) (msg: string) =
    Results.Json(Envelope.fail msg, statusCode = status)

let private haversineKm (lat1, lng1) (lat2, lng2) =
    let rad d = d * Math.PI / 180.0
    let dLat = rad (lat2 - lat1)
    let dLng = rad (lng2 - lng1)
    let a =
        sin (dLat / 2.0) ** 2.0
        + cos (rad lat1) * cos (rad lat2) * sin (dLng / 2.0) ** 2.0
    6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))

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

    app.MapPost("/login", Func<LoginRequest, AppDb, IResult>(fun req db ->
        match req.Role with
        | "Customer" ->
            match db.Customers.SingleOrDefault(fun c -> c.Name = req.Name) |> Option.ofObj with
            | Some c ->
                okJson { Token = sprintf "fake-customer-%d" c.Id
                         UserId = c.Id; Role = "Customer"; DisplayName = c.Name }
            | None -> err 404 (sprintf "No customer named %s" req.Name)
        | "Provider" ->
            match db.Providers.SingleOrDefault(fun p -> p.BusinessName = req.Name) |> Option.ofObj with
            | Some p ->
                okJson { Token = sprintf "fake-provider-%d" p.Id
                         UserId = p.Id; Role = "Provider"; DisplayName = p.BusinessName }
            | None -> err 404 (sprintf "No provider named %s" req.Name)
        | r -> err 400 (sprintf "Unknown role %s" r))) |> ignore

    app.MapGet("/services", Func<AppDb, IResult>(fun db ->
        okJson (db.Services.OrderBy(fun s -> s.Id)
                |> Seq.map (fun s -> { Id = s.Id; Name = s.Name })
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

    app.MapPost("/jobs", Func<CreateJobRequest, JobService, System.Threading.Tasks.Task<IResult>>(
        fun req svc -> task {
            let! dto = svc.Create req
            return okJson dto })) |> ignore

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
            do! hub.MessageReceived dto
            return okJson dto })) |> ignore

    app.MapGet("/ratings", Func<AppDb, int, IResult>(fun db providerId ->
        // Same (id, role) filter as toProviderDto — this is the provider's
        // public feedback, so ratings about a customer must not appear here.
        okJson (db.Ratings.Where(fun r -> r.RateeId = providerId && r.RateeRole = "Provider")
                |> Seq.map (fun r ->
                    { Id = r.Id; JobId = r.JobId
                      RaterId = r.RaterId; RaterRole = r.RaterRole
                      RateeId = r.RateeId; RateeRole = r.RateeRole
                      Stars = r.Stars; Comment = r.Comment })
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
                  Stars = req.Stars; Comment = req.Comment }
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
                  RaterId = saved.RaterId; RaterRole = saved.RaterRole
                  RateeId = saved.RateeId; RateeRole = saved.RateeRole
                  Stars = saved.Stars; Comment = saved.Comment } })) |> ignore

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
                do! hub.Notify "Payment Complete"
                return okJson { JobId = job.Id; Amount = job.Price; Status = "Transferred" } })) |> ignore
