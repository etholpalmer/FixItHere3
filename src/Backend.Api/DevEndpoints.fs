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
              Stars = 5; Comment = "Great demo!" } |> ignore
        db.SaveChanges() |> ignore
        do! apply RateAndClose
    } :> Task

let mapAll (app: WebApplication) =
    app.MapPost("/dev/reset", Func<AppDb, IResult>(fun db ->
        db.Database.EnsureDeleted() |> ignore
        db.Database.EnsureCreated() |> ignore
        FixItHere.Backend.Seed.run db
        okJson "reset")) |> ignore

    app.MapPost("/dev/demo/start",
        Func<StartDemoRequest, JobService, AppDb, IServiceProvider, Task<IResult>>(
            fun req svc db sp -> task {
                let prov = db.Providers.Single(fun p -> p.Id = req.ProviderId)
                let cust = db.Customers.Single(fun c -> c.Id = req.CustomerId)
                let! dto =
                    svc.Create
                        { CustomerId = cust.Id; ProviderId = prov.Id; ServiceId = prov.ServiceId
                          ScheduleChoice = "Now"; Lat = cust.Lat; Lng = cust.Lng
                          Address = cust.Address }
                runTimeline sp dto.Id |> ignore   // fire-and-forget scripted timeline
                return okJson dto })) |> ignore
