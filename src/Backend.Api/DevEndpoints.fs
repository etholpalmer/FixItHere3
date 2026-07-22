module FixItHere.Backend.DevEndpoints

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

[<CLIMutable>]
type StartDemoRequest = { CustomerId: int; ProviderId: int }

let private okJson (data: 't) = Results.Json(Envelope.ok data)
let private err code (msg: string) = Results.Json(Envelope.fail msg, statusCode = code)

/// Scripted demo timeline. Runs on a background task with its own DI scope.
let private runTimeline (sp: IServiceProvider) (jobId: int) =
    task {
        use scope = sp.CreateScope()
        let db = scope.ServiceProvider.GetRequiredService<AppDb>()
        let hub = scope.ServiceProvider.GetRequiredService<IBroadcaster>()
        let svc = JobService(db, hub)
        let pause () = Task.Delay 2000
        let apply ev = task { let! _ = svc.Apply jobId ev in () }
        // Hoisted: every notification below is job-scoped, so both parties are
        // needed from the first beat, not just at the interpolation step.
        let job = db.Jobs.AsNoTracking().Single(fun j -> j.Id = jobId)

        do! pause ()
        do! hub.NotifyJob ("Provider Accepted", job.CustomerId, job.ProviderId)
        do! apply Accepted
        do! pause ()
        do! apply DepartEnRoute
        // interpolate provider toward the job location in 5 steps
        let prov = db.Providers.Single(fun p -> p.Id = job.ProviderId)
        let startLat, startLng = prov.Lat, prov.Lng
        for i in 1 .. 5 do
            do! pause ()
            let t = float i / 5.0
            let lat = startLat + (job.Lat - startLat) * t
            let lng = startLng + (job.Lng - startLng) * t
            let tracked = db.Providers.Single(fun p -> p.Id = job.ProviderId)
            db.Entry(tracked).CurrentValues.SetValues({ tracked with Lat = lat; Lng = lng })
            db.SaveChanges() |> ignore
            do! hub.LocationUpdated
                    { ProviderId = prov.Id; Lat = lat; Lng = lng
                      UpdatedAt = FixItHere.Backend.Seed.nowIso () }
            if i = 2 then
                do! hub.MessageReceived
                        ({ Id = 0; JobId = jobId; SenderId = job.CustomerId
                           SenderRole = "Customer"; SenderName = "Customer"
                           Text = "Hi!"; PhotoBase64 = null
                           SentAt = FixItHere.Backend.Seed.nowIso (); Seen = false },
                         job.CustomerId, job.ProviderId)
            if i = 3 then
                do! hub.MessageReceived
                        ({ Id = 0; JobId = jobId; SenderId = job.ProviderId
                           SenderRole = "Provider"; SenderName = "Provider"
                           Text = "On my way."; PhotoBase64 = null
                           SentAt = FixItHere.Backend.Seed.nowIso (); Seen = false },
                         job.CustomerId, job.ProviderId)
        do! pause ()
        do! hub.NotifyJob ("Provider Arriving", job.CustomerId, job.ProviderId)
        do! apply Arrive
        do! pause ()
        do! apply StartWork
        do! pause ()
        do! apply CompleteWork
        do! pause ()
        do! hub.NotifyJob ("Payment Complete", job.CustomerId, job.ProviderId)
        db.Ratings.Add
            { Id = 0; JobId = jobId
              RaterId = job.CustomerId; RaterRole = "Customer"
              RateeId = job.ProviderId; RateeRole = "Provider"
              Stars = 5; Comment = "Great demo!"
              CreatedAt = Seed.nowIso () } |> ignore
        db.SaveChanges() |> ignore
        do! apply RateAndClose
    } :> Task

let mapAll (app: WebApplication) =
    app.MapPost("/dev/reset",
        Func<AppDb, Clock.DemoClockService, IBroadcaster, Task<IResult>>(fun db clock hub -> task {
            db.Database.EnsureDeleted() |> ignore
            db.Database.EnsureCreated() |> ignore
            FixItHere.Backend.Seed.run db
            // The clock resets *with* the seed, never separately. Reseeding
            // re-anchors every job at the epoch; leaving the clock hours ahead
            // would make the whole freshly-reset list instantly overdue.
            let restarted = clock.Reset()
            do! hub.ClockUpdated (Clock.toDto restarted DateTimeOffset.UtcNow)
            return okJson "reset" })) |> ignore

    /// Pull a seeded job into the live demo.
    ///
    /// Seeded jobs are untracked so an accelerated run does not march demo time
    /// past thirty grace windows and fire thirty no-show notifications in a
    /// row. This is the escape hatch the plan required alongside that flag: the
    /// operator picks the one job the story needs and opts it in.
    app.MapPost("/dev/job/{id}/track",
        Func<int, AppDb, IBroadcaster, Task<IResult>>(fun id db hub -> task {
            match db.Jobs.SingleOrDefault(fun j -> j.Id = id) |> Option.ofObj with
            | None -> return err 404 (sprintf "Job %d not found" id)
            | Some job ->
                let updated = { job with IsDemoTracked = true }
                db.Entry(job).CurrentValues.SetValues(updated)
                db.SaveChanges() |> ignore
                let dto = Services.toJobDto db updated
                do! hub.JobUpdated dto
                return okJson dto })) |> ignore

    app.MapPost("/dev/demo/start",
        Func<StartDemoRequest, JobService, AppDb, IServiceProvider, Task<IResult>>(
            fun req svc db sp -> task {
                let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
                let cust = db.Customers.Single(fun c -> c.Id = req.CustomerId)
                let clock = sp.GetRequiredService<Clock.DemoClockService>()
                let! dto =
                    svc.Create
                        { CustomerId = cust.Id; ProviderId = prov.Id; ServiceId = prov.ServiceId
                          ScheduleChoice = "Now"; Lat = cust.Lat; Lng = cust.Lng
                          Address = cust.Address }
                        (clock.Now() + BookingSlot.asapLead)
                runTimeline sp dto.Id |> ignore   // fire-and-forget scripted timeline
                return okJson dto })) |> ignore
