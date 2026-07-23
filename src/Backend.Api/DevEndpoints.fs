module FixItHere.Backend.DevEndpoints

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Backend.Db
open FixItHere.Backend.Services

[<CLIMutable>]
type StartDemoRequest =
    { CustomerId: int; ProviderId: int
      /// Run the running-late beat: the provider proposes a new arrival and
      /// the customer answers it live. This is the scene the pitch turns on.
      Late: bool }

let private okJson (data: 't) = Results.Json(Envelope.ok data)
let private err code (msg: string) = Results.Json(Envelope.fail msg, statusCode = code)

/// Scripted demo timeline.
///
/// `late = true` runs the beat the whole plan is built around: the provider
/// hits traffic, proposes a new arrival, and the customer's phone lights up
/// while the audience watches.
let private runTimeline (sp: IServiceProvider) (jobId: int) (late: bool) =
    task {
        use scope = sp.CreateScope()
        let db = scope.ServiceProvider.GetRequiredService<AppDb>()
        let hub = scope.ServiceProvider.GetRequiredService<IBroadcaster>()
        let clock = sp.GetRequiredService<Clock.DemoClockService>()
        let lifetime = sp.GetRequiredService<IHostApplicationLifetime>()
        let ct = lifetime.ApplicationStopping
        let svc = JobService(db, hub)
        let apply ev = task { let! _ = svc.Apply jobId ev in () }

        /// Wait for demo time to advance, not for real time to pass.
        ///
        /// This is what keeps the map honest against the countdown. A fixed
        /// `Task.Delay 2000` paces the car in real seconds while every deadline
        /// on both phones is measured in demo minutes, so at 60x the countdown
        /// sprints to zero while the provider crawls.
        ///
        /// Polling rather than computing one delay is deliberate: the operator
        /// can pause, change rate or jump *during* a step, and a precomputed
        /// delay would ignore all three. Pausing the clock pauses the demo;
        /// jumping forward completes the current step at once, which is exactly
        /// what "skip ahead" should feel like.
        let waitDemo (span: TimeSpan) =
            task {
                let until = clock.Now() + span
                // 100 ms is below the clients' 250 ms repaint, so the car never
                // lags the countdown by a frame anyone can see.
                while clock.Now() < until && not ct.IsCancellationRequested do
                    do! Task.Delay(100, ct)
            }

        // Hoisted: every notification below is job-scoped, so both parties are
        // needed from the first beat, not just at the interpolation step.
        let job = db.Jobs.AsNoTracking().Single(fun j -> j.Id = jobId)
        let prov = db.Providers.AsNoTracking().Single(fun p -> p.Id = job.ProviderId)

        /// Real chat, persisted through the same table the apps read.
        ///
        /// This used to broadcast `MessageDto`s with `Id = 0` that were never
        /// written: the second was deduped away, both vanished on navigation,
        /// and the customer-role one rendered in the *customer's own app* as
        /// "You: Hi!" — words they never typed. Only the provider speaks in the
        /// script now, because a scripted message attributed to the person
        /// holding the phone is the loudest possible tell.
        let say (text: string) =
            task {
                let msg =
                    { Id = 0; JobId = jobId
                      SenderId = job.ProviderId; SenderRole = "Provider"
                      Text = text; PhotoBase64 = null
                      SentAt = (clock.Now()).ToString "o"; Seen = false }
                db.Messages.Add msg |> ignore
                db.SaveChanges() |> ignore
                let saved = db.Messages.OrderByDescending(fun m -> m.Id).First()
                do! hub.MessageReceived
                        ({ Id = saved.Id; JobId = saved.JobId
                           SenderId = saved.SenderId; SenderRole = saved.SenderRole
                           SenderName = prov.BusinessName
                           Text = saved.Text; PhotoBase64 = saved.PhotoBase64
                           SentAt = saved.SentAt; Seen = saved.Seen },
                         job.CustomerId, job.ProviderId)
            }

        do! waitDemo (TimeSpan.FromMinutes 1.0)
        do! hub.NotifyJob ("Provider Accepted", job.CustomerId, job.ProviderId)
        do! apply Accepted

        if late then
            // The hook. Propose a later arrival before departing, so the
            // customer's phone lights up while nothing else is happening on
            // screen and the request is unmistakably the event.
            do! waitDemo (TimeSpan.FromMinutes 1.0)
            do! say "Sorry — stuck behind a closure on the DVP. Can I push us back 15?"
            let now = clock.Now()
            let current = Services.readReschedule job
            let proposal =
                { ProposedStart = current.PromisedStart.AddMinutes 15.0
                  By = ActorRole.Provider
                  Reason = "Traffic on the DVP"
                  ExpiresAt = now + Reschedule.proposalWindow }
            match! svc.Reschedule jobId now (Propose proposal) with
            | Ok _ -> do! hub.NotifyJob ("Provider is running late", job.CustomerId, job.ProviderId)
            | Error e -> do! hub.NotifyJob (sprintf "Could not propose a new time: %s" e,
                                            job.CustomerId, job.ProviderId)
            // Left pending on purpose. The customer answers it — that is the
            // beat. If nobody does, task 9's expiry sweep lapses it.
            do! waitDemo Reschedule.proposalWindow

        do! waitDemo (TimeSpan.FromMinutes 1.0)
        do! apply DepartEnRoute
        do! say "On my way — should be about ten minutes."
        // One travel interpolator, shared with the real in-app Depart. Awaited
        // here because the script applies Arrive only once the car has arrived.
        do! Movement.driveEnRoute sp jobId

        do! hub.NotifyJob ("Provider Arriving", job.CustomerId, job.ProviderId)
        do! apply Arrive
        do! waitDemo (TimeSpan.FromMinutes 2.0)
        do! apply StartWork
        do! waitDemo (TimeSpan.FromMinutes 45.0)
        do! apply CompleteWork
        do! waitDemo (TimeSpan.FromMinutes 1.0)
        do! hub.NotifyJob ("Payment Complete", job.CustomerId, job.ProviderId)
        // Stops here, deliberately.
        //
        // The script used to write its own 5-star "Great demo!" review *and*
        // apply RateAndClose while the customer app was already sitting on its
        // Rating screen — so the rating the audience typed went nowhere, and
        // "Great demo!" appeared in the provider's public feedback. Handing
        // control back at payment is also the better moment to hand it back:
        // it is exactly when someone wants to try the app themselves.
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
                runTimeline sp dto.Id req.Late |> ignore   // fire-and-forget scripted timeline
                return okJson dto })) |> ignore
